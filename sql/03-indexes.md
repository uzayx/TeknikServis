# Index Önerileri ve Gerekçeleri

> **Metodoloji:** Her öneri, `sql/02-reports.sql` içindeki gerçek bir sorguya bağlanır ve
> `EXPLAIN (ANALYZE, BUFFERS)` ile index öncesi/sonrası planı karşılaştırılarak doğrulanır.
> Doğrulanamayan öneriler, nedeniyle birlikte raporlanır.
>
> **Veri hacmi:** 4.000 ticket, ~13.600 durum geçmişi, ~5.800 yorum, ~2.800 ek (`sql/01-seed.sql`).
> `setseed(0.42)` ile ölçümler tekrarlanabilir.
>
> **Ölçüm notu:** Bu hacimde tüm veri RAM'e sığar, bu yüzden `Execution Time` farkları yanıltıcı
> olabilir. Asıl göstergeler: **erişim yöntemi**, **cost** (özellikle startup) ve **buffer sayısı**.
> Üretim hacminde buffer farkı doğrudan disk I/O'suna ve süreye yansır.

## Önce dürüst bir tespit: bu hacimde index'e ihtiyaç yok

4.000 ticket ≈ 1 MB, tamamı RAM'de. Index'siz de her sorgu milisaniyeler sürüyor.
Bu öneriler **gelecek hacim** içindir: `docs/mimari.md` projeksiyonuna göre 3 yılda
~1M ticket, ~6M history.

**Tetikleyici: ~100.000 satır.** O noktadan önce eklenmeli — 1M satırda `CREATE INDEX`
tabloyu kilitler ve panikle karar verilir (üretimde `CONCURRENTLY` şart).

**Ölçümün sınırı:** Bu hacimde `Execution Time` farkları yanıltıcıdır (veri zaten RAM'de).
Asıl gösterge **erişim yöntemi** ve **buffer sayısı** — bunlar hacimden bağımsız olarak
geçerli, süre farkı değil.

---

## Denetim: 16 index üretilmişti, 12'ye indirdim

EF Core her **foreign key** için otomatik index üretir (PostgreSQL üretmez; sadece PK ve
UNIQUE için üretir). Sebep: FK index'i olmadan parent tarafına `DELETE`/`UPDATE` yapmak,
referans bütünlüğü kontrolü için tam tarama gerektirir.

Bu kolaylık sessizce birikir. Şema tamamlandığında 16 index vardı. **Hepsini denetledim:**

```sql
SELECT relname, indexrelname, idx_scan, pg_size_pretty(pg_relation_size(indexrelid))
FROM pg_stat_user_indexes WHERE schemaname = 'public' ORDER BY idx_scan;
```

Ölçüm, gerçek iş yükü sonrası alındı: **50 adımlık uçtan uca smoke test + 6 rapor sorgusu +
tipik mobil sorguları.** (İlk denemede sayaçlar boştu — `docker compose down -v` istatistikleri
sıfırlamıştı. Ölçüm ortamının doğru olduğunu doğrulamak, ölçümün kendisi kadar önemli.)

### Kaldırılanlar

| Index | `idx_scan` | Neden ölü |
|---|---|---|
| `IX_customers_LastName` | 0 | Arama `FirstName + ' ' + LastName` concat'ine `LIKE '%...%'` uyguluyor — b-tree bu deseni **hiçbir zaman** kullanamaz |
| `IX_technicians_LastName` | 0 | Aynı |
| `IX_technicians_IsActive` | 0 | 15 satırlık tabloda 14'ü `true` — seçicilik yok |
| `IX_service_tickets_Status` | 0 | `(Status, AssignedTechnicianId)` composite'inin öneki. **Kanıt:** composite `idx_scan=3`, tekil `0` — planlayıcı her zaman composite'i seçiyor |

**Doğrulama:** Silme sonrası sıfırdan kurulum + seed + 50/50 smoke test yeşil. Ölü oldukları kanıtlandı.

### Ölü ama tutulanlar

| Index | `idx_scan` | Neden kaldı |
|---|---|---|
| `IX_ticket_status_histories_NewTechnicianId` | 0 | Bu kolonla sorgu atmıyoruz. Ama teknisyen silinmeye kalkarsa referans kontrolü (FK `Restrict`) tam tarama yapardı — 6M satırlık history'de kabul edilemez |
| `IX_ticket_status_histories_PreviousTechnicianId` | 0 | Aynı |
| `IX_attachments_ServiceTicketId` | 0 | Ek listesi endpoint'i ölçüm sırasında çağrılmadı; gerçek kullanımı var |

### En çok kullanılanlar

| Index | `idx_scan` |
|---|---|
| `PK_service_tickets` | 22.257 |
| `IX_ticket_status_histories_ServiceTicketId_ChangedAt` | **51** |
| `IX_service_tickets_CustomerId` | 40 |
| `IX_service_tickets_CreatedAt` | 4 |

History index'inin en çok kullanılan ikinci index olması, Adım 1'deki tasarım kararını
doğruluyor: her ticket detayı ve funnel sorgusu bu index'ten geçiyor.

**İlke:** Her şeyi index'lersen hiçbir şey index değildir. Her index bir yazma maliyeti, disk/cache
alanı ve bakım yüküdür. ORM'in ürettiklerini denetlemek, kendi önerilerini ölçmek kadar gereklidir.

---
## Seçicilik tablosu (kararların dayanağı)

| Filtre | Satır | Oran |
|---|---|---|
| `Status NOT IN (4,5)` (aktif kayıtlar) | 2.940 | **%73.5** |
| `Status IN (0,1)` (bekleyenler) | 1.475 | %36.9 |
| `CompletedAt IS NOT NULL` (tamamlananlar) | 1.723 | %43.1 |

Hiçbir filtre yüksek seçicilikte değil — bu, aşağıdaki bulguların çoğunu açıklıyor.

---

## A) Mevcut index'ler (EF Core migration ile) ve savunmaları

| Index | Kolon(lar) | Desteklediği sorgu | Gerekçe |
|---|---|---|---|
| `IX_service_tickets_Status_AssignedTechnicianId` | Status, AssignedTechnicianId | Sorgu 1, Sorgu 3 | Ölçümlerde **zaten devrede** olduğu görüldü: `Status IN (0,1)` filtresi Bitmap Index Scan ile karşılanıyor. Composite sırada önce eşitlik filtresi, sonra gruplama kolonu |
| `IX_service_tickets_CreatedAt` | CreatedAt | Liste varsayılan sıralaması | Sayfalamalı listenin ORDER BY'ı |
| `IX_ticket_status_histories_ServiceTicketId_ChangedAt` | ServiceTicketId, ChangedAt | Sorgu 5 (funnel) | `PARTITION BY ServiceTicketId ORDER BY ChangedAt` window'unun birebir karşılığı |
| `IX_customers_Email`, `IX_technicians_Email` (unique) | Email | İş kuralı | Tekillik (409 DATA_INTEGRITY_VIOLATION) |
| `IX_service_tickets_TicketNumber` (unique) | TicketNumber | `?search=` | İnsan-okur numara ile arama |

---

## B) Öneri 1 ✅ — Covering index: SLA taraması ve aktif kayıt listeleri

```sql
CREATE INDEX ix_tickets_sla_covering
ON service_tickets ("SlaDeadline")
INCLUDE ("TicketNumber", "Status", "Priority", "CompletedAt");
```

**Hedef:** Sorgu 2A/2B (SLA ihlalleri) + operasyon ekranı ("en acil aktif kayıtlar").

**Mantık:** Sorgu `SlaDeadline` üzerinden filtreler/sıralar ama yanıtta birkaç kolon daha okur.
`INCLUDE` ile bu kolonlar index yaprağına eklenir → PostgreSQL heap'e hiç gitmeden
**Index Only Scan** yapabilir. (Görünürlük haritası güncel olmalı: `VACUUM ANALYZE`.)

**Ölçüm — SLA ihlal sorgusu (`WHERE SlaDeadline < NOW() ORDER BY SlaDeadline LIMIT 100`):**

| | Erişim | Cost | Buffers | Exec |
|---|---|---|---|---|
| Önce | `Seq Scan` + `Sort` (top-N heapsort) | 304.95..305.20 | 140 | 0.973 ms |
| **Sonra** | **`Index Only Scan`, `Heap Fetches: 0`** | **0.28..5.67** | **3** | **0.047 ms** |

**Kazanç:** `Heap Fetches: 0` — sorgu tabloya **hiç dokunmadı**, tüm veri index yaprağından geldi.
Buffer **47 kat**, execution **20 kat** düştü. Sort düğümü kalktı (index zaten sıralı, `LIMIT`
ilk 100 satırı alıp duruyor). Startup cost 304.95 → 0.28 — sayfalamalı listelerde belirleyici olan bu.

**Maliyeti:** 280 kB ile tablonun en büyük index'i (4 ek kolon yaprakta tutulur) ve her
INSERT/UPDATE bu index'i de günceller. SLA raporu sık çalışan bir operasyon ekranıysa takas kârlı;
nadir çalışan bir yönetim raporu olsaydı gerekmeyebilirdi.

---

## C) Öneri 2 ✅ — Covering partial index: teknisyen performans raporu

```sql
CREATE INDEX ix_tickets_tech_completed
ON service_tickets ("AssignedTechnicianId", "CompletedAt")
INCLUDE ("AssignedAt")
WHERE "CompletedAt" IS NOT NULL AND "AssignedAt" IS NOT NULL;
```

**Hedef:** Sorgu 4 (performans raporu).

**Ölçüm — iki varyant:**

| | Erişim | Gruplama | Cost | Buffers | Exec |
|---|---|---|---|---|---|
| Index yok | `Seq Scan` | HashAggregate | 201.21..201.38 | 140 | 0.846 ms |
| Varyant A: `(TechId, CompletedAt)` partial | `Seq Scan` (**yok sayıldı**) | HashAggregate | 201.21..201.38 | 140 | 1.019 ms |
| **Varyant B: + `INCLUDE (AssignedAt)`** | **`Index Only Scan`, `Heap Fetches: 0`** | **GroupAggregate** | **0.28..86.87** | **13** | **0.654 ms** |

**Varyant A neden başarısız oldu:** Sorgu `CompletedAt - AssignedAt` hesaplıyor ama `AssignedAt`
index'te yoktu. Index scan yapılsa bile her satır için heap'e gitmek gerekecekti — index'in tek
avantajı ortadan kalkıyordu. Planlayıcı "o zaman baştan seq scan yaparım" dedi. Seçicilik de
zayıf (%43.1) ve `LIMIT` yok.

**Varyant B neden kazandı:** `AssignedAt`'in `INCLUDE`'a eklenmesi index'i covering yaptı →
`Heap Fetches: 0`. Bonus: **HashAggregate → GroupAggregate** dönüşümü — index veriyi zaten
`AssignedTechnicianId` sıralı verdiği için hash tablosu kurmaya gerek kalmadı.

**Not:** Execution farkı mütevazı (0.85 → 0.65 ms) çünkü veri RAM'de. Asıl gösterge
**buffers 140 → 13** (11 kat az blok okuma).

---

## D) Öneri 3 ❌ — Partial index: kanıtla reddedildi

```sql
-- ONERILMIYOR
CREATE INDEX ix_tickets_active
ON service_tickets ("SlaDeadline")
WHERE "Status" NOT IN (4, 5);
```

**Hipotez:** Tablo zamanla kapanmış kayıtlarla dolar; operasyonel sorgular yalnızca aktif
kayıtlara bakar. Partial index sadece aktif satırları içerir → küçük index, az I/O, sıcak cache.

**Ölçüm — 4 varyant izole edildi** (`WHERE Status NOT IN (4,5) ORDER BY SlaDeadline LIMIT 50`):

| | Erişim | Cost | Buffers | Exec |
|---|---|---|---|---|
| **A)** Hiçbir yeni index yok | `Seq Scan` + `Sort` | 287.66..287.79 | 140 | 1.035 ms |
| **B)** Sadece partial | `Index Scan using ix_tickets_active` | 0.28..**11.30** | 51 | 0.090 ms |
| **C)** Sadece covering | `Index Only Scan`, `Heap Fetches: 0` | 0.28..**3.85** | **3** | 0.057 ms |
| **D)** İkisi birden | **`Index Only Scan using ix_tickets_sla_covering`** | 0.28..3.85 | 3 | 0.046 ms |

**Sonuç:** Partial index **tek başına çalışıyor** (B: sort ve filter kalkıyor, 11 kat hızlanma).
Ancak covering index varken planlayıcı ona **dönüp bakmıyor** (D). Sebep buffer'da: partial her
satır için heap'e gitmek zorunda (51 buffer), covering hiç gitmiyor (3 buffer) → cost 11.30 vs 3.85.

**Ek gerekçe — seçicilik:** `Status NOT IN (4,5)` tablonun **%73.5**'i. "Küçük index" vaadi bu
veride geçersiz. Hipotez, tablonun zamanla kapalı kayıtlarla dolacağını varsayıyordu; 6 aylık
sentetik veride bu dağılım henüz oluşmamış. **Üretimde 2-3 yıl sonra** kapalı oranı %80'e
çıktığında öneri yeniden değerlendirilmeli — ama bugünkü veriyle savunulamaz.

**Boyut:** partial 88 kB, covering 280 kB. Partial daha küçük, ama küçük olması işe
yaramasını sağlamıyor.

---

## E) Aynı index, farklı sorgu şekli: window function tuzağı

Partial index, **window function içeren** Sorgu 3'ün tam haline
(`ROW_NUMBER() OVER (PARTITION BY "Priority" ORDER BY "SlaDeadline")`) uygulandığında da
yok sayıldı. `(Status, SlaDeadline)` composite varyantı da denendi — üç plan da **birebir aynı**
çıktı (cost 342.57, Bitmap Index Scan + 2× Sort).

**Neden:**
1. **Seçicilik yetersiz:** `Status IN (0,1)` = %36.9. Üçte birini okuyacaksan bitmap scan optimal.
2. **Sıralama zaten kayboluyor:** Window function `(Priority, SlaDeadline)` sıralı bir sort'u
   zorunlu kılıyor. Veri `SlaDeadline` sıralı gelse bile o sıra `WindowAgg`'de yok oluyor;
   dıştaki `ORDER BY` için ikinci sort kaçınılmaz. Index'in besleyebileceği tek şey boşa gidiyor.

**Çıkarım:** Index tablo için değil, **sorgu şekli için** tasarlanır. Aynı index bir sorguyu
11 kat hızlandırırken diğerine hiç dokunmayabilir.

---

## F) Bilinçli olarak önerilmeyenler

- **`Title` üzerinde b-tree:** `?search=` contains (`%...%`) araması yapar; b-tree önek olmayan
  aramada devreye girmez. Doğru araç `pg_trgm` + GIN; bu hacimde ek uzantı bağımlılığına değmez.
- **Her FK'ya körlemesine index:** EF Core FK kolonlarına zaten index üretiyor. Denetlendi (yukarı bkz.): 4'ü ölü çıkıp kaldırıldı, 3'ü referans bütünlüğü için tutuldu.
- **`Priority` üzerinde tek başına index:** 4 değerli düşük kardinalite; tek başına seçiciliği yok.

---

## G) Özet

| Aday | Sonuç | Kanıt |
|---|---|---|
| `ix_tickets_sla_covering` | ✅ **Öneriliyor** | Index Only Scan, buffers 140 → 3, cost 304.95 → 5.67 |
| `ix_tickets_tech_completed` (INCLUDE'lu) | ✅ **Öneriliyor** | Index Only Scan + GroupAggregate, buffers 140 → 13 |
| `ix_tickets_tech_completed` (INCLUDE'suz) | ❌ Reddedildi | Planlayıcı yok saydı — `AssignedAt` index'te yok |
| `ix_tickets_active` (partial) | ❌ Reddedildi | Tek başına çalışıyor ama covering varken yok sayılıyor; seçicilik %73.5 |
| `ix_tickets_active` (window'lu sorguda) | ❌ Reddedildi | Window function sıralamayı yok ediyor |

**Sonuç: 3 aday değerlendirildi, 2'si önerildi.** Beş ölçümün üçü hipotezi çürüttü.

**Metodolojik not:** İlk ölçüm turunda "index yok" durumunu ölçerken başka bir index'in ortamda
kaldığı fark edildi (plan çıktısı `Index Only Scan using ix_tickets_sla_covering` gösteriyordu).
Her varyant izole edilerek ölçüm tekrarlandı. A/B ölçümünde "önce" durumunun gerçekten önce
olduğu plan çıktısından doğrulanmalıdır.

---

## H) Ölçüm yöntemi

```sql
DROP INDEX IF EXISTS ...;                 -- ortami izole et
VACUUM ANALYZE service_tickets;
EXPLAIN (ANALYZE, BUFFERS) SELECT ... ;   -- ONCE (plani dogrula: gercekten index yok mu?)

CREATE INDEX ... ;
VACUUM ANALYZE service_tickets;           -- Index Only Scan icin gorunurluk haritasi sart
EXPLAIN (ANALYZE, BUFFERS) SELECT ... ;   -- SONRA
```

Plan okurken bakılan noktalar: erişim yöntemi (Seq Scan / Index Scan / **Index Only Scan**),
`Heap Fetches` (0 ise covering çalışıyor), ayrı bir `Sort` düğümünün kalkıp kalkmadığı,
`Buffers: shared hit/read`, ve cost içindeki **startup** değeri (LIMIT'li sorgularda belirleyici).

