# Production Incident Runbook — CPU, Timeout, Deadlock

> Bölüm 8 teslimatı. Senaryo: üretim ortamında CPU tavan yapmış, istekler timeout'a düşüyor,
> PostgreSQL log'unda deadlock kayıtları var.
>
> Bu doküman genel bir "incident yönetimi" özeti değil. Bu sistemin **ölçülmüş** zayıflıklarına
> dayanıyor (`sql/03-indexes.md`, `docs/mimari.md`) — çünkü hangi sorgunun CPU yakacağını ve
> hangi iki işlemin kilit yarışına gireceğini zaten biliyoruz.

---

## 0. Temel İlke: Önce Durdur, Sonra Anla

Incident anında en sık yapılan hata, kök nedeni ararken kanamanın devam etmesidir.
**Sıra şu:**

| Aşama | Süre hedefi | Amaç |
|---|---|---|
| 1. Tespit | 0-2 dk | Gerçekten bizde mi? Etki alanı ne? |
| 2. Stabilize | 2-15 dk | Kullanıcıya hizmeti geri ver (kök neden bilinmese bile) |
| 3. Teşhis | 15-60 dk | Kök nedeni bul, kanıtla |
| 4. Kalıcı çözüm | Sonra | Sakin kafayla, test edilerek |
| 5. Postmortem | 1-2 gün içinde | Suçsuz, yazılı, aksiyon maddeli |

Stabilize aşamasında "geçici çözüm" utanç kaynağı değildir. Rollback, ölçekleme, feature
flag kapatma — hepsi meşru. Kök nedeni gece 03:00'te doğru bulma olasılığın düşük, ama
sistemi ayağa kaldırma olasılığın yüksek.

---

## 1. Tespit (0-2 dk)

**İlk sorular:**

```
Ne zaman başladı?              → deploy zamanıyla örtüşüyor mu?
Etki alanı nedir?              → tüm endpoint'ler mi, tek endpoint mi?
Hangi kullanıcı grubu?         → mobil mi, merkez mi, hepsi mi?
Yakın zamanda ne değişti?      → deploy, migration, config, trafik artışı, seed/import
```

**Bakılacak yerler (`docs/mimari.md` Adım 2'de kurulan gözlemlenebilirlik):**

```
Grafana/Prometheus  → RPS, p95/p99 gecikme, hata oranı, CPU, DB pool kullanımı
Seq/Loki            → son 15 dk hata log'ları, correlation ID ile gruplu
PostgreSQL          → pg_stat_activity, pg_stat_statements
```

**"Yakın zamanda ne değişti?" sorusu tek başına vakaların çoğunu çözer.** Bu projede
bunun canlı örneği var: `docs/notlar.md` V14'te arama tamamen çöktü — sebep bir gün önce
yapılan tek satırlık bir "iyileştirme"ydi (`ToLower` → `ToLowerInvariant`). Birim testler
yeşildi, kimse şüphelenmedi. Değişiklik zaman çizelgesine bakmadan saatlerce sorgu planı
incelenebilirdi.

---

## 2. Stabilize (2-15 dk)

Kök neden bilinmeden yapılabilecekler, tercih sırasıyla:

| Eylem | Ne zaman | Risk |
|---|---|---|
| **Rollback** | Sorun deploy'la başladıysa | En güvenli. İlk seçenek. |
| **Sorunlu sorguyu kes** | Tek bir uzun sorgu her şeyi kilitliyorsa | Düşük — o kullanıcı hata alır |
| **Feature flag kapat** | Yeni özellik suçluysa | Düşük |
| **Yatay ölçekle** | Yük artışı doğrulandıysa | Orta — DB'ye daha çok bağlantı gider, D3'ü tetikleyebilir |
| **Rate limit** | Anormal trafik / bot | Orta — meşru kullanıcı da etkilenir |
| **Restart** | Son çare | **Kanıtı siler.** Önce dump/log al. |

**Uzun süren sorguyu kesme:**

```sql
-- 30 saniyeden uzun surenler
SELECT pid, now() - query_start AS sure, state, left(query, 100)
FROM pg_stat_activity
WHERE state = 'active' AND now() - query_start > interval '30 seconds'
ORDER BY sure DESC;

-- Nazikce iptal et (once bunu dene)
SELECT pg_cancel_backend(<pid>);

-- Baglantiyi tamamen kapat (cancel ise yaramazsa)
SELECT pg_terminate_backend(<pid>);
```

**Restart'a dair uyarı:** "Kapat aç" çoğu zaman işe yarar ama kanıtı yok eder. Restart'tan
önce: `pg_stat_activity` çıktısı, .NET dump (`dotnet-dump collect`), son 15 dk log'ları
kaydedilmeli. Aksi halde aynı incident bir hafta sonra tekrarlanır ve elinde hiçbir şey olmaz.

---

## 3. Teşhis — CPU

### Önce ayır: CPU nerede yanıyor?

```
API container'ında mı, PostgreSQL container'ında mı?
```

Bu ayrım her şeyi belirler. `docker stats` veya Grafana ile 10 saniyede cevaplanır.
Yanlış tarafa bakarak yarım saat harcamak, incident'larda en sık kaybedilen zamandır.

### Senaryo A — PostgreSQL CPU'su tavan (bu sistemde EN OLASI)

**Neden en olası:** `docs/mimari.md` D1'de ölçülmüş bir zayıflık var. `?search=` sorgusu
`%...%` deseni kullanıyor, hiçbir b-tree index'i kullanamıyor, tam tarama yapıyor. 4.000
satırda 1 ms; 1 milyon satırda ~300 ms. Zirvede eşzamanlı 10 arama = 10 tam tarama.

**Doğrulama:**

```sql
-- Toplam sureye gore en pahali sorgular (pg_stat_statements uzantisi gerekli)
SELECT
    left(query, 120) AS sorgu,
    calls,
    round(total_exec_time::numeric, 1) AS toplam_ms,
    round(mean_exec_time::numeric, 2)  AS ortalama_ms,
    rows
FROM pg_stat_statements
ORDER BY total_exec_time DESC
LIMIT 15;
```

Beklenen görüntü: `LIKE '%...%'` içeren sorgu üstlerde, yüksek `calls` × orta `mean_exec_time`.
**Tuzak:** En yavaş sorgu değil, `calls × mean` çarpımı en büyük olan sorgu CPU'yu yakar.
Günde 3 kez çalışan 2 saniyelik rapor, saniyede 10 kez çalışan 200 ms'lik aramadan zararsızdır.

**Şüpheli sorgunun planını al:**

```sql
EXPLAIN (ANALYZE, BUFFERS)
SELECT ... FROM service_tickets st
JOIN customers c ON ...
WHERE LOWER(st."Title") LIKE '%kombi%' ...;
```

`Seq Scan` + yüksek `Buffers: shared read` görürsen teşhis doğrulanmıştır.

**Anlık çözüm:** Arama endpoint'ini geçici olarak devre dışı bırak veya minimum karakter
sınırı koy (3 harften kısa arama reddedilsin — en pahalı taramalar bunlar).

**Kalıcı çözüm:** `docs/mimari.md` Adım 0 — `pg_trgm` + GIN index, denormalize arama kolonu.

### Senaryo B — API CPU'su tavan

**Şüpheliler:**

| Belirti | Olası neden |
|---|---|
| GC süresi yüksek, Gen2 sık | Bellek baskısı — büyük yanıtlar? `pageSize` sınırı aşıldı mı? |
| Thread pool starvation | Senkron I/O (`.Result`, `.Wait()`) — async zincir kırılmış |
| Belirli endpoint'te yoğunlaşma | N+1 sorgu, döngüde DB çağrısı |

**Araçlar:**

```bash
# Canli metrikler
dotnet-counters monitor --process-id <pid> --counters System.Runtime,Microsoft.AspNetCore.Hosting

# Kritik gostergeler:
#   ThreadPool Queue Length yuksek + CPU dusuk  -> starvation (senkron I/O)
#   GC Heap Size surekli artiyor                -> sizinti
#   % Time in GC > 20                           -> allocation baskisi

# Dump al (once bunu, restart'tan ONCE)
dotnet-dump collect --process-id <pid> --output /tmp/api.dmp
```

**Bu projedeki koruma:** `pageSize` üst sınırı 100'e sabitlenmiş (`TicketQueryParameters`),
sorgu servisleri `AsNoTracking` + projection kullanıyor (tam entity yüklemiyor). Bu ikisi
API tarafı CPU/bellek sorunlarının en sık iki sebebini baştan kapatıyor.

---

## 4. Teşhis — Timeout

Timeout bir semptomdur, hastalık değil. Üç farklı hastalığın aynı belirtisi:

```
1. Baglanti havuzu tukendi    -> istek DB baglantisi BEKLIYOR
2. Sorgu yavas                -> istek CEVAP bekliyor
3. Kilit bekleme              -> istek baska transaction'i bekliyor
```

**Ayırt etme:**

```sql
-- Ne yapiyorlar? Bekliyorlarsa neyi bekliyorlar?
SELECT
    pid,
    state,
    wait_event_type,
    wait_event,
    now() - query_start AS sure,
    left(query, 80) AS sorgu
FROM pg_stat_activity
WHERE datname = 'teknikservis_db' AND state <> 'idle'
ORDER BY sure DESC;
```

**Okuma kılavuzu:**

| `wait_event_type` | Anlamı | Yön |
|---|---|---|
| `Lock` | Başka transaction'ın kilidini bekliyor | → Bölüm 5 (deadlock/kilit) |
| `IO` | Diskten okuyor | → Index eksik veya tablo çok büyük |
| `Client` | Uygulamadan komut bekliyor | → Uygulama tarafı sorun |
| (boş, `active`) | Gerçekten çalışıyor | → Sorgu yavaş, `EXPLAIN` al |

**Bağlantı havuzu kontrolü:**

```sql
SELECT count(*) AS aktif_baglanti,
       (SELECT setting::int FROM pg_settings WHERE name = 'max_connections') AS ust_sinir
FROM pg_stat_activity;
```

`docs/mimari.md` D3'te hesaplandı: bugün 19 RPS için ~1-6 bağlantı yeterli, havuz (100) rahat.
**Ama** yatay ölçeklendikten sonra her instance kendi havuzunu açar: 4 instance × 100 = 400 >
`max_connections = 100`. Yatay ölçekleme sonrası timeout görülüyorsa **ilk şüpheli budur** ve
çözüm PgBouncer'dır.

**"Idle in transaction" tehlikesi:**

```sql
SELECT pid, now() - state_change AS ne_kadardir, left(query, 80)
FROM pg_stat_activity
WHERE state = 'idle in transaction'
ORDER BY ne_kadardir DESC;
```

Bu durum, açılmış ama commit/rollback edilmemiş transaction demektir — kilitleri tutmaya
devam eder ve başkalarını bekletir. Genellikle kod hatasıdır (exception sonrası
rollback edilmemiş, `using` unutulmuş).

---

## 5. Teşhis — Deadlock

### PostgreSQL'in deadlock davranışı

PostgreSQL deadlock'ı otomatik tespit eder (varsayılan 1 saniye sonra), taraflardan birini
seçip `40P01` hatasıyla iptal eder. Yani deadlock sistemi kilitlemez — **ama** iptal edilen
işlem kullanıcıya hata olarak döner. Bizim `GlobalExceptionMiddleware`'imizde bu
`DbUpdateException` → 409 olarak dönüyor. Kullanıcı "işlem başarısız" görür.

### Log'u oku

```sql
-- Deadlock loglamasi acik mi?
SHOW log_lock_waits;      -- 'on' olmali
SHOW deadlock_timeout;    -- varsayilan '1s'
```

```bash
docker logs teknikservis-postgres 2>&1 | grep -A 20 "deadlock detected"
```

Tipik çıktı:

```
ERROR:  deadlock detected
DETAIL:  Process 1234 waits for ShareLock on transaction 5678; blocked by process 5679.
         Process 5679 waits for ShareLock on transaction 5680; blocked by process 1234.
HINT:  See server log for query details.
CONTEXT:  while updating tuple (0,42) in relation "service_tickets"
```

**Dikkat edilecekler:** hangi iki `relation`, hangi tuple, hangi iki sorgu. Neredeyse her
zaman **kilit alma sırasının tutarsızlığı** vardır: A işlemi önce X sonra Y kilitliyor,
B işlemi önce Y sonra X.

### Bu sistemdeki gerçek risk

Şu an tek `SaveChanges` kullanıyoruz (kodda yorumu var: EF tek `SaveChanges`'i zaten tek
transaction'da çalıştırır), bu deadlock riskini düşürüyor. Yine de iki senaryo var:

**Senaryo 1 — Eşzamanlı atama:**

```
İşlem A: POST /tickets/{id}/assign     → service_tickets UPDATE, sonra history INSERT
İşlem B: POST /tickets/{id}/status     → service_tickets UPDATE, sonra history INSERT
```

İkisi de aynı ticket satırına yazıyor. EF, `SaveChanges` içinde işlemleri entity tipine göre
sıralar — bu sıra tutarlı olduğu sürece deadlock oluşmaz. Ama bir gün "toplu atama" gibi
çok satıra dokunan bir özellik eklenirse (`UPDATE ... WHERE technicianId = X`), satır sırası
farklı olacağı için deadlock kapıda demektir.

**Senaryo 2 — Rapor + yazma çakışması:**

`sql/02-reports.sql` içindeki uzun window function sorguları okuma kilidi tutar. PostgreSQL'de
MVCC sayesinde okuyucu yazanı engellemez — ama uzun süren rapor `VACUUM`'u geciktirir, tablo
şişer, performans düşer. `docs/mimari.md` Adım 7'de read replica çözümü bu yüzden var.

### Deadlock'a karşı kalıcı önlemler

| Önlem | Açıklama |
|---|---|
| **Tutarlı kilit sırası** | Çok satıra dokunan işlemler her zaman aynı sırayla (örn. `ORDER BY "Id"`) |
| **Kısa transaction** | Transaction içinde HTTP çağrısı, dosya işlemi, kullanıcı beklemesi **asla** |
| **Retry politikası** | `40P01` geçici bir hatadır; exponential backoff ile 2-3 kez denenmelidir. `EnableRetryOnFailure` zaten açık — ama deadlock kodunun listede olduğu doğrulanmalı |
| **Idempotency** | Retry güvenli olsun diye (`docs/mimari.md` Adım 6) |

---

## 6. AI'dan Nasıl Destek Alınır

### Kırmızı çizgiler (önce bunlar)

| ❌ Asla | Neden |
|---|---|
| Üretim verisi yapıştırmak | KVKK/GDPR ihlali. Log'da müşteri adı, telefon, e-posta var |
| Connection string, secret, token | Sızıntı riski |
| "Şu komutu çalıştırayım mı?" diye sorup körü körüne uygulamak | AI üretimin ne olduğunu bilmiyor |
| Teşhisi AI'a **verdirmek** | AI hipotez üretir, doğrulamaz |

**Log paylaşırken önce anonimleştir:** isimleri, telefonları, e-postaları, GUID'leri
`<REDACTED>` ile değiştir. Sorgu planı ve hata yapısı korunur, kişisel veri gitmez.

### Faydalı prompt örnekleri

**Deadlock log'u yorumlatma:**

```
Asagidaki PostgreSQL deadlock log'unda (veriler anonimlestirildi) hangi iki islem
hangi sirayla kilit aliyor? Kilit sirasi tutarsizligi var mi?

ERROR:  deadlock detected
DETAIL:  Process 1234 waits for ShareLock on transaction 5678; blocked by process 5679.
         Process 5679 waits for ShareLock on transaction 5680; blocked by process 1234.
CONTEXT:  while updating tuple (0,42) in relation "service_tickets"

Tablolar: service_tickets (Id PK), ticket_status_histories (ServiceTicketId FK).
EF Core kullaniyoruz, tek SaveChanges cagrisi var.
Bana ne yapmam gerektigini soyleme; once ne gordugunu ve hangi bilgiyi
eksik buldugunu soyle.
```

Son cümle kritik: AI'ı çözüm üretmeye değil, **gözlem yapmaya** zorluyor.

**EXPLAIN planı okutma:**

```
Bu EXPLAIN (ANALYZE, BUFFERS) ciktisinda neden Seq Scan secilmis?
Tabloda 1.2M satir var, WHERE kosulu ~450K satir donduruyor.
Index eklemenin ise yarayip yaramayacagini plandaki hangi degerlere bakarak
anlarim? Tahmin degil, cikti uzerinden goster.
```

**Thread pool teşhisi:**

```
dotnet-counters ciktisi: ThreadPool Queue Length surekli 200+, CPU %25,
ThreadPool Thread Count artiyor. .NET 9 / ASP.NET Core.
Bu tablo hangi hipotezleri destekler, hangilerini eler?
Her hipotez icin hangi ek olcumu almam gerektigini yaz.
```

**Kod incelemesi (incident sonrası):**

```
Bu servis metodunda deadlock'a yol acabilecek kilit sirasi problemi var mi?
Transaction siniri nerede basliyor ve bitiyor?
[kod]
```

**Postmortem yazımı:**

```
Asagidaki olay zaman cizelgesinden suclamasiz (blameless) bir postmortem taslagi
cikar. Bolumler: etki, zaman cizelgesi, kok neden, tespiti neyin geciktirdigi,
aksiyon maddeleri.
[anonimlestirilmis zaman cizelgesi]
```

### AI'ın sınırları — bu projeden kanıt

Bu projede AI ile çalışırken 14 vaka belgelendi (`docs/notlar.md`). İkisi doğrudan
incident yönetimiyle ilgili:

**V4 — AI hipotez üretir, doğrulamaz.** `assign` endpoint'i 409 `CONCURRENCY_CONFLICT`
veriyordu. AI sırayla üç teşhis koydu: `xmin` yapılandırması, `ConcurrencyStamp`, EF paket
sürüm uyumsuzluğu. **Üçü de yanlıştı.** Her biri için refactor yapıldı, sorun devam etti.
Çözüm, tahmin etmeyi bırakıp `EnableSensitiveDataLogging` ile gerçek SQL'i okumaktan geldi:
EF, `INSERT` yerine `UPDATE` üretiyordu. Kök neden tek satırdı (navigation koleksiyonuna
PK'lı nesne eklemek).

> **Ders:** AI'ın ürettiği hipotez inandırıcıdır — çünkü tutarlı bir hikâye anlatır. Ama
> hikâyenin doğru olduğunu **ölçüm** söyler. Incident anında AI'ın en tehlikeli kullanımı,
> onun ilk hipotezine kilitlenmektir. Hata mesajındaki kelimeye ("concurrency") saplanmak
> iki gereksiz refactor'a mal oldu.

**V14 — Testin yeşil olması "çalışıyor" demek değil.** Bir düzeltme (`ToLower` →
`ToLowerInvariant`) arama özelliğini gerçek veritabanında tamamen çökertti, ama 53 birim
testi yeşil kaldı — InMemory sağlayıcı sorguyu istemci tarafında çalıştırdığı için farkı
göremiyordu. Hatayı gerçek PostgreSQL'e karşı koşan uçtan uca smoke test yakaladı.

> **Ders:** Incident'ta "ama testler geçiyordu" cümlesi bir savunma değil, bir ipucudur:
> test ortamı üretimden nerede sapıyor?

### Incident'ta AI'ın doğru rolü

| ✅ İyi | ❌ Kötü |
|---|---|
| Komut hatırlatıcısı (`pg_stat_activity` sorgusu nasıldı?) | Karar verici |
| Log/plan yorumlayıcısı | Tek hipoteze kilitleyici |
| Hipotez **listesi** üretici (eleme senin) | Hipotez **onaylayıcı** |
| Postmortem taslağı yazıcı | Üretim verisi deposu |

**Özetle:** AI, incident'ta bir junior mühendis gibidir — hızlı, bilgili, kendinden emin ve
bazen tamamen yanlış. Söylediğini doğrulamadan uygulamazsın.

---

## 7. Postmortem

Incident kapandıktan sonra 1-2 gün içinde, **suçsuz** (blameless) formatta:

```markdown
## Etki
Hangi kullanıcılar, ne kadar süre, hangi işlevi kaybetti? (sayıyla)

## Zaman Çizelgesi
14:02  Deploy #142
14:18  p95 gecikme 200ms → 3s
14:23  Alarm tetiklendi          ← 5 dakika gecikme: neden?
14:31  Rollback başladı
14:36  Metrikler normale döndü

## Kök Neden
(5 Neden — teknik sebebe kadar in)

## Tespiti Ne Geciktirdi?
Genellikle en değerli bölüm burasıdır.

## Aksiyonlar
| Madde | Sahip | Tarih |
```

**Suçsuz olmasının sebebi:** Suçlanan mühendis bir daha detay paylaşmaz, bir sonraki
incident daha uzun sürer. Amaç kişiyi değil sistemi düzeltmektir — "X yanlış kod yazdı"
değil, "yanlış kodun üretime çıkmasını hiçbir kontrol engellemedi".

---

## 8. Bu Sistem İçin Hızlı Referans

| Belirti | İlk bakılacak | En olası neden (bu sistemde) |
|---|---|---|
| PostgreSQL CPU %100 | `pg_stat_statements` | **`?search=` tam taraması** (D1, ölçüldü) |
| API CPU %100, DB normal | `dotnet-counters` | N+1, GC baskısı, senkron I/O |
| Timeout, CPU normal | `pg_stat_activity` → `wait_event` | Bağlantı havuzu (yatay ölçekleme sonrası) |
| Yaygın 409 | Uygulama log'u, `errorCode` | Deadlock retry'ı veya iş kuralı ihlali |
| Bellek sürekli artıyor | `dotnet-counters` GC | Sızıntı, büyük yanıt |
| Yavaş ama hatasız | `EXPLAIN (ANALYZE, BUFFERS)` | Index eksik / plan değişti |
| Deploy sonrası bozuldu | **Rollback**, sonra diff | Değişikliğin kendisi (V14 örneği) |

**Bu sistemin bilinen zayıflıkları — incident öncesi kabul edilmiş:**

1. `?search=` tam tarama yapar → `docs/mimari.md` D1, çözüm Adım 0
2. Tek instance → tek arıza noktası, çözüm Adım 1
3. Raporlar OLTP'de → çözüm Adım 7 (read replica)
4. Migration startup'ta (bayraklı) → çok instance'ta yarış, pipeline'a taşınmalı

Bunları önceden yazmış olmak, incident anında yarım saat kazandırır.