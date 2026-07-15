# Index Önerileri ve Gerekçeleri

> **Metodoloji:** Her öneri, `sql/02-reports.sql` içindeki gerçek bir sorguya bağlanır.
> Ölçüm `EXPLAIN (ANALYZE, BUFFERS)` ile index öncesi/sonrası plan karşılaştırılarak yapılmıştır.
>
> **Veri hacmi:** 4.000 ticket, 13.836 durum geçmişi, 5.935 yorum, 2.842 ek (bkz. `sql/01-seed.sql`).
>
> **Ölçüm notu:** Bu hacimde tüm veri RAM'e sığdığı için `Execution Time` farkları görece küçüktür.
> Bu yüzden asıl gösterge olarak **erişim yöntemi**, **cost** ve **buffer sayısı** kullanılmıştır;
> üretim hacminde (100K+ satır) buffer farkı doğrudan disk I/O'suna ve süreye yansır.

---

## A) Mevcut index'ler (EF Core migration ile oluşturuldu) ve savunmaları

| Index | Kolon(lar) | Desteklediği sorgu | Gerekçe |
|---|---|---|---|
| `IX_service_tickets_Status_AssignedTechnicianId` | Status, AssignedTechnicianId | Sorgu 1, Sorgu 3 | Ölçümlerde bu index'in **zaten devrede** olduğu görüldü: `Status IN (0,1)` filtresi Bitmap Index Scan ile karşılanıyor. Composite sırada önce eşitlik filtresi (Status), sonra gruplama kolonu. |
| `IX_service_tickets_Status` | Status | Liste filtresi `?status=` | Tek değerli status filtrelerinde doğrudan erişim |
| `IX_service_tickets_CreatedAt` | CreatedAt | Liste varsayılan sıralaması (`createdAt desc`) | Sayfalamalı listenin ORDER BY'ını besler |
| `IX_ticket_status_histories_ServiceTicketId_ChangedAt` | ServiceTicketId, ChangedAt | Sorgu 5 (funnel) | `PARTITION BY ServiceTicketId ORDER BY ChangedAt` window'unun birebir karşılığı; sort adımını ortadan kaldırır |
| `IX_customers_Email`, `IX_technicians_Email` (unique) | Email | Uygulama kuralı | Tekillik garantisi (409 DATA_INTEGRITY_VIOLATION) |
| `IX_service_tickets_TicketNumber` (unique) | TicketNumber | `?search=` | İnsan-okur numara ile arama |

---

## B) Önerilen yeni index'ler

### Öneri 1 — Partial index: aktif kayıtlar

```sql
CREATE INDEX ix_tickets_active
ON service_tickets ("SlaDeadline")
WHERE "Status" NOT IN (4, 5);
```

**Hedef:** Operasyon ekranı — "en acil aktif kayıtlar" listesi (`?status=...&sortBy=slaDeadline`).

**Mantık:** Servis sistemlerinde tablo zamanla kapanmış kayıtlarla dolar; bir yıl sonra
satırların büyük çoğunluğu Approved/Closed olur, ancak operasyonel sorguların tamamı
aktif kayıtlara bakar. Partial index yalnızca aktif satırları içerir: daha küçük index,
daha az I/O, daha sıcak cache. Kapalı kayıtlar index bakım maliyetine de girmez.

**Ölçüm — hedef sorgu (window function'sız, LIMIT 50):**

| | Erişim yöntemi | Cost | Buffers | Execution |
|---|---|---|---|---|
| Index yok | `Seq Scan` + `Sort` (top-N heapsort) | 287.20..287.33 | 144 | 0.874 ms |
| **Partial index** | **`Index Scan using ix_tickets_active`** | **0.28..11.46** | **52** | **0.166 ms** |

**Kazanç üç katmanlı:**
1. **Sort düğümü tamamen kalktı** — index zaten `SlaDeadline` sıralı; `LIMIT 50` ilk 50 satırı alıp duruyor (`actual rows=50`, tabloda 2.896 aday varken).
2. **Filter kalktı** — partial index'in `WHERE` yüklemi sorgunun WHERE'iyle örtüştüğü için planlayıcı filtreyi index'in kendisine soğurdu; çalışma zamanında Status kontrolü yapılmıyor.
3. **Startup cost 287 → 0.28** — sayfalamalı listelerde asıl kazanç budur: ilk satır anında geliyor.

### ⚠️ Aynı index, farklı sorgu şekli: neden çalışmadı

Aynı index, **window function içeren** Sorgu 3'ün tam haline (`ROW_NUMBER() OVER (PARTITION BY "Priority" ORDER BY "SlaDeadline")`) uygulandığında **planlayıcı tarafından tamamen yok sayıldı.**
`(Status, SlaDeadline)` composite varyantı da denendi — sonuç değişmedi:

| Varyant | Plan | Cost |
|---|---|---|
| Index yok | Bitmap Index Scan (mevcut index) + 2× Sort | 339.02..339.15 |
| `(SlaDeadline)` partial | **aynı plan** | 339.02..339.15 |
| `(Status, SlaDeadline)` partial | **aynı plan** | 339.02..339.15 |

**Neden:**
1. **Seçicilik yetersiz:** `Status IN (0,1)` 1.441 satır = tablonun **%36'sı**. Üçte birini okuyacaksan bitmap scan zaten optimal; planlayıcı haklı.
2. **Sıralama zaten kayboluyor:** Window function `(Priority, SlaDeadline)` sıralı bir sort'u zorunlu kılıyor. Veri `SlaDeadline` sıralı gelse bile o sıra `WindowAgg`'de yok oluyor; dıştaki `ORDER BY` için ikinci sort kaçınılmaz. Tek kolonlu index'in besleyebileceği tek şey baştan boşa gidiyor.

**Çıkarım:** Index tablo için değil, **sorgu şekli için** tasarlanır. Aynı index bir sorguyu
5 kat hızlandırırken diğerine hiç dokunmayabilir. Bu yüzden her öneri, hedeflediği sorgunun
planı ölçülerek doğrulanmalıdır.

---

### Öneri 2 — Covering index: SLA ihlal taraması

```sql
CREATE INDEX ix_tickets_sla_covering
ON service_tickets ("SlaDeadline")
INCLUDE ("TicketNumber", "Status", "Priority", "CompletedAt");
```

**Hedef:** Sorgu 2A / 2B (SLA ihlalleri).

**Mantık:** İhlal sorgusu `SlaDeadline` üzerinden filtreler ama yanıtta birkaç kolon daha okur.
`INCLUDE` ile bu kolonlar index yaprağına eklenir; PostgreSQL heap'e hiç gitmeden
**Index Only Scan** yapabilir. (Görünürlük haritasının güncel olması şart: `VACUUM ANALYZE`.)

**Ölçüm:**

| | Erişim yöntemi | Cost | Buffers | Execution |
|---|---|---|---|---|
| Önce | `Seq Scan` + `Sort` | 353.69..353.94 | 144 | 1.031 ms |
| **Sonra** | **`Index Only Scan`, `Heap Fetches: 0`** | **0.28..5.54** | **3** | **0.102 ms** |

**Sonuç:** En net kazanç bu öneride. `Heap Fetches: 0` — sorgu tabloya **hiç dokunmadı**,
tüm veri index yaprağından geldi. Buffer sayısı **48 kat**, execution time **10 kat** düştü.

**Maliyeti:** Index boyutu büyür (4 ek kolon yaprakta tutulur) ve `service_tickets` üzerindeki
her INSERT/UPDATE bu index'i de günceller. SLA raporu sık çalıştırılan bir operasyon ekranıysa
takas kârlıdır; nadir çalışan bir yönetim raporuysa gerekmeyebilir.

---

### Öneri 3 — Covering partial index: teknisyen performans raporu

```sql
CREATE INDEX ix_tickets_tech_completed
ON service_tickets ("AssignedTechnicianId", "CompletedAt")
INCLUDE ("AssignedAt")
WHERE "CompletedAt" IS NOT NULL AND "AssignedAt" IS NOT NULL;
```

**Hedef:** Sorgu 4 (performans raporu).

**Ölçüm — iki varyant denendi:**

| | Erişim | Gruplama | Cost | Buffers | Execution |
|---|---|---|---|---|---|
| Index yok | `Seq Scan` | HashAggregate | 203.05..203.22 | 141 | 1.038 ms |
| Varyant A: `(TechId, CompletedAt)` partial | `Seq Scan` (**yok sayıldı**) | HashAggregate | 203.05..203.22 | 141 | 1.089 ms |
| **Varyant B: + `INCLUDE (AssignedAt)`** | **`Index Only Scan`, `Heap Fetches: 0`** | **GroupAggregate** | **0.28..88.55** | **13** | **0.798 ms** |

**Varyant A neden başarısız oldu:** Sorgu `CompletedAt - AssignedAt` hesaplıyor ama `AssignedAt`
index'te yoktu. Index scan yapılsa bile her satır için heap'e gitmek gerekecekti — index'in tek
avantajı ortadan kalkıyordu. Planlayıcı haklı olarak "o zaman baştan seq scan yaparım" dedi.
Ayrıca seçicilik zayıf: 1.786/4.000 = **%45**, ve `LIMIT` yok (gruplama tüm satırları okumak zorunda).

**Varyant B neden kazandı:** `AssignedAt`'in `INCLUDE`'a eklenmesi index'i covering yaptı →
`Heap Fetches: 0`. Bonus olarak **HashAggregate → GroupAggregate** dönüşümü: index veriyi zaten
`AssignedTechnicianId` sıralı verdiği için hash tablosu kurmaya gerek kalmadı, sıralı akış
doğrudan gruplandı.

**Not:** Execution time farkı mütevazı (1.04 → 0.80 ms) çünkü bu hacimde veri zaten RAM'de.
Asıl gösterge **buffers 141 → 13** (11 kat az blok okuma); üretim hacminde bu doğrudan I/O'ya yansır.

---

## C) Bilinçli olarak önerilmeyenler

- **`Title` üzerinde b-tree:** `?search=` sorgusu contains (`%...%`) araması yapar; b-tree önek
  olmayan aramada devreye girmez. Doğru araç `pg_trgm` + GIN index'tir; bu case'in hacminde ek
  uzantı bağımlılığına değmez. Arama hacmi artarsa ilk adım budur.
- **Her FK'ya körlemesine index:** Npgsql, FK kolonlarına zaten index üretti. Ek index'ler yazma
  maliyetini artırır, okuma kazancı sağlamaz.
- **`Priority` üzerinde tek başına index:** Düşük kardinalite (4 değer); tek başına seçiciliği yok,
  composite içinde anlamlı olabilir.

---

## D) Özet

| Öneri | Sonuç | Ana kazanç |
|---|---|---|
| 1 — `ix_tickets_active` | ✅ Kanıtlandı (window'suz sorguda) | Sort ve Filter düğümleri kalktı, startup cost 287 → 0.28 |
| 1 — window function'lı sorguda | ❌ Etkisiz | Seçicilik %36 + window sıralamayı yok ediyor |
| 2 — `ix_tickets_sla_covering` | ✅ Kanıtlandı | Index Only Scan, buffers 144 → 3 |
| 3 — Varyant A | ❌ Etkisiz | `AssignedAt` index'te yok → heap'e gitmek zorunda |
| 3 — Varyant B (`INCLUDE`) | ✅ Kanıtlandı | Index Only Scan + GroupAggregate, buffers 141 → 13 |

**Metodolojik çıkarım:** Beş ölçümün ikisi hipotezi çürüttü. Bu doküman, "şu index'i ekleyin"
listesi değil; her önerinin hedef sorgunun planıyla doğrulandığı — ve doğrulanamayanların
nedeniyle birlikte raporlandığı — bir çalışmadır.

## E) Ölçüm yöntemi

```sql
EXPLAIN (ANALYZE, BUFFERS) SELECT ... ;   -- once
CREATE INDEX ... ;
VACUUM ANALYZE service_tickets;           -- Index Only Scan icin gorunurluk haritasi sart
EXPLAIN (ANALYZE, BUFFERS) SELECT ... ;   -- sonra
```

Plan okurken bakılan noktalar: erişim yöntemi (Seq Scan / Index Scan / **Index Only Scan**),
`Heap Fetches` (0 ise covering çalışıyor), ayrı bir `Sort` düğümünün kalkıp kalkmadığı,
`Buffers: shared hit/read`, ve `cost` içindeki **startup** değeri (LIMIT'li sorgularda belirleyici).
