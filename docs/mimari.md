# Mimari — 20.000 Günlük Aktif Kullanıcı Senaryosu

> Bölüm 7 teslimatı. Mevcut sistem: tek .NET 9 instance + tek PostgreSQL,
> `docker compose` ile ayağa kalkan monolit.

---

## Yönetici Özeti

Hesap yapmadan mimari kararı vermek, ölçmeden index önermeye benzer (bkz. `sql/03-indexes.md`).
Bu yüzden önce sayıları çıkardım. Sonuç şaşırtıcı:

**20.000 DAU ≈ 20 istek/saniye zirve.** Bu sayı tek bir .NET instance için düşük.
Sistemi yıkacak olan kullanıcı sayısı değil, **veri büyümesi** ve **ölçülmüş bir sorgu zayıflığı**.

Dolayısıyla bu doküman "her şeyi ekleyelim" listesi değil. Sırası şu:

| Öncelik | Değişiklik | Neden | Ne zaman |
|---|---|---|---|
| 0 | `pg_trgm` + GIN index (arama) | **Ölçülmüş** zayıflık: arama tam tarama yapıyor | 100K satırdan önce |
| 1 | Health check + graceful shutdown + 2 instance | Tek instance = tek arıza noktası | Hemen |
| 2 | Structured logging + correlation ID + metrikler | Görmeden yönetilmez | Hemen |
| 3 | Redis (referans veri cache) | Ölçülen okuma yükü | RPS 50'yi geçince |
| 4 | Queue + Background Job (bildirim, SLA taraması) | Senkron işler istek süresini uzatıyor | Bildirim özelliği gelince |
| 5 | Outbox | Bildirim kaybını önlemek | Queue ile birlikte |
| 6 | Idempotency | Mobil çift-tıklama, ağ retry'ı | Queue ile birlikte |
| 7 | Read replica | Raporlar OLTP'yi yavaşlatınca | 500K satır sonrası |
| — | Mikroservis, event sourcing, CQRS | **Gerekmiyor** (gerekçe: bölüm 10) | — |

---

## 1. Yük Hesabı

### Varsayımlar (açıkça yazıyorum ki tartışılabilsin)

| Varsayım | Değer | Gerekçe |
|---|---|---|
| DAU | 20.000 | Verilen |
| Kullanıcı dağılımı | ~19.000 müşteri, ~800 teknisyen, ~200 merkez | Teknik servis iş modeli |
| Kullanıcı başına istek/gün | ~15 | Uygulama açılışı + liste + 1-2 detay + aksiyon |
| Ticket açan müşteri oranı | %5 | Herkes her gün arıza bildirmez |
| Ticket başına durum değişimi | 6 | New→Assigned→InProgress→Completed→Approved→Closed |
| Ticket başına yorum | ~1.5 | Mevcut seed dağılımından |
| Mesai yoğunlaşması | %80'i 9 saatte | 09:00-18:00 |
| Zirve saat çarpanı | 2.5× | Sabah açılışı |

### Hesap
**Okuma/yazma oranı ≈ 40:1.** Bu, mimarinin en belirleyici sayısı: sistem ezici çoğunlukla
okuma yapıyor. Yazma tarafı için özel bir şey yapmaya gerek yok; okuma tarafı optimize edilmeli.

### Veri büyümesi (asıl tehdit burada)

| Tablo | Yıllık artış | 3 yıl sonra |
|---|---|---|
| service_tickets | ~350.000 | ~1.000.000 |
| ticket_status_histories | ~2.100.000 | ~6.300.000 |
| comments | ~500.000 | ~1.500.000 |
| attachments (metadata) | ~250.000 | ~750.000 |

Bugün 4.000 ticket'la çalışıyoruz. **Üç yıl sonra 250 kat.** Kullanıcı sayısı sabit kalsa
bile tablo büyür. Bu, aşağıdaki darboğaz analizinin merkezinde.

---

## 2. Mevcut Sistemin Gerçek Darboğazları

Tahmin değil, ölçüm: `sql/03-indexes.md` içindeki `EXPLAIN (ANALYZE, BUFFERS)` çalışmaları.

### 🔴 D1 — Arama sorgusu tam tarama yapıyor (en kritik)

`GET /api/tickets?search=...` şu sorguyu üretiyor:

```sql
WHERE LOWER("TicketNumber") LIKE '%kombi%'
   OR LOWER("Title")        LIKE '%kombi%'
   OR LOWER(c."FirstName" || ' ' || c."LastName") LIKE '%kombi%'
   ...
```

`%...%` deseni **hiçbir b-tree index'i kullanamaz**. Bu bilinen ve `sql/03-indexes.md`'de
bilinçli olarak kabul edilmiş bir kısıt.

| Satır sayısı | Beklenen süre | Zirvede 19 RPS'te |
|---|---|---|
| 4.000 (bugün) | ~1 ms | Sorun yok |
| 100.000 | ~30 ms | Sınırda |
| 1.000.000 (3 yıl) | **~300+ ms** | **Çöker** |

Üstelik dört kolonda `LIKE` + iki JOIN. Zirvede eşzamanlı 10 arama = 10 tam tarama =
CPU doygunluğu + bağlantı havuzu tükenmesi. **Bu, sistemin ilk kırılacağı yer.**

**Çözüm — Adım 0 (mimari değişiklik değil, index):**

```sql
CREATE EXTENSION pg_trgm;

-- Aranan alanlari tek bir ifadede birlestiren GIN index
CREATE INDEX ix_tickets_search_trgm ON service_tickets
USING GIN ((("TicketNumber" || ' ' || "Title")) gin_trgm_ops);
```

Müşteri/teknisyen adı JOIN'li olduğu için trigram index doğrudan kapsayamaz. İki seçenek:
1. **Denormalize arama kolonu:** `service_tickets.search_vector` — ticket kaydedilirken
   müşteri+teknisyen adı da yazılır, trigram index onun üzerine kurulur. Yazma maliyeti düşük
   (7.000/gün), okuma kazancı büyük (300.000/gün). **Okuma/yazma oranı 40:1 olduğu için doğru takas.**
2. **PostgreSQL full-text search** (`tsvector` + GIN) — kelime bazlı arama, `%içinde%` değil.
   Daha hızlı ama "kısmi kelime" aramasını kaybederiz.

Öneri: (1). Ölçmeden karar vermem — üretim benzeri hacimde `EXPLAIN ANALYZE` ile doğrulanmalı.

### 🟡 D2 — Tek instance = tek arıza noktası

Bugün API tek container. Deploy = kesinti. Crash = kesinti. Bu bir kapasite sorunu değil
(19 RPS tek instance için düşük), **erişilebilirlik** sorunu.

### 🟡 D3 — Bağlantı havuzu

Npgsql varsayılan `Maximum Pool Size = 100`. Hesap:
Havuz bugün sorun değil. **Ama** yatay ölçeklemede her instance kendi havuzunu açar:
4 instance × 100 = 400 bağlantı. PostgreSQL varsayılan `max_connections = 100`.
**Yatay ölçeklerken PgBouncer şart olur** — instance sayısı arttıkça DB bağlantı sayısı
çarpan etkisiyle büyür.

### 🟡 D4 — Raporlar OLTP veritabanında

`sql/02-reports.sql` içindeki window function'lı sorgular (funnel analizi 13.000 history
satırını tarıyor) bugün milisaniyeler sürüyor. 6 milyon satırda saniyeler sürecek ve aynı
veritabanında müşteri isteklerini bekletecek.

### 🟢 D5 — Senkron işlemler

Şu an her şey istek-yanıt içinde. Bildirim/e-posta özelliği yok — ama gelecek. Bunlar
senkron eklenirse `POST /assign` çağrısı bir push servisinin yavaşlığına bağımlı hale gelir.

---

## 3. Adım 1 — Yatay Ölçekleme ve Erişilebilirlik

**Tetikleyici:** Hemen. Kapasite için değil, kesintisiz deploy ve arıza toleransı için.

**Yapılacaklar:**
- **En az 2 API instance** + load balancer. API zaten stateless (session yok, in-memory
  state yok) — bu sayede yatay ölçekleme bedava geliyor. Bu, baştan bilinçli bir karardı.
- **Health check endpoint'leri ayrıştır:**
  - `/health/live` → süreç ayakta mı (DB'ye bakmaz)
  - `/health/ready` → trafiği kabul edebilir mi (DB bağlantısı kontrol edilir)
  Mevcut `/health/db` readiness'e karşılık geliyor; liveness ayrı olmalı, yoksa DB kısa süre
  yanıt vermediğinde orkestratör sağlıklı süreçleri gereksiz yere öldürür.
- **Graceful shutdown:** Kubernetes SIGTERM gönderir; devam eden istekler bitirilmeli.
- **Migration'ı startup'tan çıkar.** Şu an `Database__ApplyMigrationsOnStartup` bayrağıyla
  container ayağa kalkarken migrate ediyoruz — bu compose için pratik ama **çok instance'lı
  ortamda yarış koşulu**: iki instance aynı anda migrate etmeye çalışır. Migration deployment
  pipeline'ında ayrı bir adım olmalı. (Kodda bu not zaten yorum olarak var.)

**Ne kadar instance?** 19 RPS için 2 yeter (biri yedek). Kapasite değil, erişilebilirlik kararı.

---

## 4. Adım 2 — Logging ve Monitoring

**Tetikleyici:** Hemen. Görülmeyen sistem yönetilemez; incident anında (Bölüm 8) elimizde
veri olmalı.

### Logging

Mevcut durum: `GlobalExceptionMiddleware` hatalara `traceId` ekliyor ve ProblemDetails
döndürüyor. Bu temel var, üzerine inşa edilecek:

- **Serilog + structured logging.** Metin değil, alan bazlı log:

Fark kritik: ikincisinde "son 1 saatte Closed'a geçen ticket'lar" diye sorgu atılabilir.
- **Correlation ID:** Mobil istekle birlikte `X-Correlation-Id` gönderir (yoksa API üretir).
  Bu ID API log'undan queue mesajına, oradan background job'a kadar taşınır. Bir kullanıcı
  şikâyetinde tüm zinciri tek ID ile izlemek mümkün olur.
- **Log seviyeleri:** Üretimde `Information`; `Debug` sadece geçici tanılama için.
  Bu projede yaşadığımız EF change-tracking bug'ında (`docs/notlar.md` V4)
  `EnableSensitiveDataLogging` açıp SQL'i okumak çözümü getirdi — ama o ayar **asla üretimde
  kalmamalı**, parametre değerlerini (kişisel veri) log'a basar. Geçici tanılama aracı,
  kalıcı ayar değil.
- **Merkezi toplama:** Seq / Elasticsearch / Loki. Çok instance'da `docker logs` işe yaramaz.

### Monitoring

**OpenTelemetry** ile üç sinyal:

| Sinyal | İçerik |
|---|---|
| Metrics | RPS, p50/p95/p99 gecikme, hata oranı, DB havuz kullanımı, GC |
| Traces | HTTP → EF sorgusu → queue → job zinciri (dağıtık izleme) |
| Logs | Structured, correlation ID ile bağlı |

**Kritik ayrım — teknik metrik vs iş metriği.** Çoğu sistem sadece CPU/RAM izler. Bizim
sistemimizin sağlığı bunlarla ölçülmez:

| İş metriği | Neden önemli | Alarm eşiği (örnek) |
|---|---|---|
| **SLA ihlal oranı** | Sistemin var oluş amacı | %40'ı geçerse |
| Atanmamış kritik ticket sayısı | Operasyonel körlük | 10'u geçerse |
| Ortalama atama süresi | Kuyruk şişiyor mu | Trend bazlı |
| Teknisyen yük dengesizliği | `sql/02-reports.sql` Sorgu 1 | PERCENT_RANK sapması |

CPU %20'de gayet iyi görünen bir sistemde SLA ihlal oranı %80 olabilir — teknik olarak
sağlıklı, iş olarak çökmüş. **Alarm iş metriğine kurulur.**

---

## 5. Adım 3 — Redis

**Tetikleyici:** Zirve RPS 50'yi geçince veya p95 gecikme 200ms'yi aşınca. Bugün gerekmez —
ama ne zaman gerekeceğini bilmek gerekir.

### Neyi cache'leyeceğiz (ve neyi cache'lemeyeceğiz)

| Veri | Cache? | TTL | Gerekçe |
|---|---|---|---|
| Teknisyen listesi | ✅ | 5 dk | ~800 kayıt, günde birkaç kez değişir, **her atama ekranında okunur** |
| Müşteri listesi (arama için) | ⚠️ Kısmi | 1 dk | 19.000 kayıt — tümünü cache'lemek yerine sık aranan sayfalar |
| Ticket **listesi** | ❌ | — | Sürekli değişiyor; cache invalidation maliyeti kazançtan fazla |
| Ticket **detayı** | ❌ | — | Aynı sebep + tutarsız veri riski (teknisyen eski durumu görürse yanlış işlem yapar) |
| SLA yapılandırması | ✅ | 1 saat | `appsettings.json`'dan okunuyor, neredeyse hiç değişmez |
| Rapor sonuçları | ✅ | 15 dk | Pahalı sorgular, anlık taze olması gerekmez |

**Cache'lememe kararı, cache'leme kararı kadar önemli.** Bir teknik servis sisteminde
teknisyenin ekranında bayat durum görmesi = yanlış işlem = veri tutarsızlığı. Ticket verisi
cache'lenmez.

### Invalidation stratejisi

- **TTL öncelikli** (basit, öngörülebilir). Referans veri için 1-5 dk bayatlık kabul edilebilir.
- **Yazma anında invalidate:** yeni teknisyen eklenince `technicians:all` anahtarı silinir.
- **Cache stampede koruması:** TTL dolduğu anda 50 istek aynı anda DB'ye gitmesin diye
  `SemaphoreSlim` ya da Redis kilidi ile tek istek yenilesin, diğerleri beklesin.

### Redis'in ikinci işi

Cache'ten daha kritik olabilir: **dağıtık kilit ve idempotency deposu** (bölüm 8).
Çok instance'lı ortamda "aynı ticket'a iki instance aynı anda teknisyen atamasın" gibi
koordinasyon için gerekir.

---

## 6. Adım 4 — Queue ve Background Job

**Tetikleyici:** Bildirim/e-posta özelliği geldiğinde. Bugün bu özellik yok — ama mimarinin
buna hazır olması gerekiyor.

### Hangi işler senkron olmaktan çıkmalı?

| İş | Neden asenkron | Gecikme toleransı |
|---|---|---|
| Teknisyene push bildirimi (atama) | Push servisi 3. parti, yavaşlarsa `POST /assign` yavaşlar | Saniyeler |
| Müşteriye SMS/e-posta (durum değişimi) | Aynı | Saniyeler-dakika |
| SLA yaklaşıyor uyarısı | Zamanlanmış tarama, isteğe bağlı değil | Dakika |
| Rapor üretimi (PDF/Excel) | Saniyeler sürebilir | Dakika |
| Attachment küçük resim üretimi | CPU yoğun | Dakika |

**Kural:** Kullanıcının cevabı beklemek zorunda olmadığı her iş kuyruğa gider.
Kullanıcı "atama yapıldı" cevabını bildirim gönderilmesini beklemeden almalı.

### Teknoloji seçimi

| Seçenek | Artı | Eksi | Karar |
|---|---|---|---|
| **Hangfire** (PostgreSQL storage) | Ek altyapı yok, dashboard hazır, cron desteği, retry built-in | DB'ye yük bindirir | ✅ **Bu ölçek için** |
| RabbitMQ / Azure Service Bus | Gerçek broker, yüksek hacim | Ek altyapı, işletme maliyeti | Hacim artarsa |
| Kafka | Event streaming, replay | Bu ihtiyaç için aşırı | ❌ |

**Hangfire seçiyorum.** Gerekçe: günde ~7.000 iş = saniyede 0.08 iş. Bunun için ayrı bir
broker işletmek, çözdüğünden fazla operasyonel yük getirir. PostgreSQL bu hacmi kuyruk
olarak taşır. Hacim 10 kat artarsa RabbitMQ'ya geçilir — o zaman da Outbox pattern'i
sayesinde uygulama kodu değişmez, sadece dispatcher değişir.

### Zamanlanmış işler (Background Job)
**Çok instance'ta tuzak:** 4 instance varsa cron işi 4 kez çalışır. Hangfire bunu
distributed lock ile çözer; RabbitMQ kullanılsaydı ayrıca ele alınmalıydı.

---

## 7. Adım 5 — Outbox Pattern

**Tetikleyici:** Queue ile aynı anda. Queue'yu Outbox'sız eklemek, sessiz veri kaybı üretir.

### Problem: iki sistem, tek transaction yok

`POST /api/tickets/{id}/assign` çağrısında:

```csharp
ticket.AssignedTechnicianId = technicianId;
_db.TicketStatusHistories.Add(history);
await _db.SaveChangesAsync(ct);        // ✅ PostgreSQL commit

await _queue.PublishAsync(new TechnicianAssignedEvent(...));  // ❌ ya burada crash olursa?
```

**Sonuç:** Veritabanında atama var, teknisyene bildirim yok. Teknisyen işten haberdar
olmaz, SLA ihlal edilir, kimse fark etmez. Sessiz başarısızlık — en tehlikelisi.

**Ters sıra da çözmez:** Önce mesaj gönderip sonra commit edersek, commit başarısız olduğunda
teknisyene var olmayan bir iş bildirilir.

Kök sebep: PostgreSQL ve mesaj kuyruğu **aynı transaction'ı paylaşamaz**. Dağıtık transaction
(2PC) teorik olarak mümkün ama pratikte kırılgan ve yavaş.

### Çözüm: mesajı da veritabanına yaz

```sql
CREATE TABLE outbox_messages (
    "Id"            uuid PRIMARY KEY,
    "OccurredAt"    timestamptz NOT NULL,
    "Type"          varchar(200) NOT NULL,
    "Payload"       jsonb NOT NULL,
    "ProcessedAt"   timestamptz NULL,
    "Error"         text NULL,
    "RetryCount"    int NOT NULL DEFAULT 0
);

-- Islenmemis mesajlar icin partial index: tablo buyudukce
-- (islenmis mesajlar birikir) tarama maliyeti sabit kalir.
CREATE INDEX ix_outbox_pending ON outbox_messages ("OccurredAt")
WHERE "ProcessedAt" IS NULL;
```

```csharp
// Atama ve mesaj AYNI transaction'da
ticket.AssignedTechnicianId = technicianId;
_db.TicketStatusHistories.Add(history);
_db.OutboxMessages.Add(new OutboxMessage {
    Type = "TechnicianAssigned",
    Payload = JsonSerializer.Serialize(new { ticket.Id, technicianId })
});
await _db.SaveChangesAsync(ct);   // Ya ikisi de yazilir ya hicbiri
```

Ayrı bir dispatcher (Hangfire recurring job, ~5 sn periyot) `ProcessedAt IS NULL` olanları
okur, kuyruğa basar, `ProcessedAt` işaretler.

### Bizim mimarimize oturması

**Önemli avantaj:** `ticket_status_histories` tablomuz zaten bir olay günlüğü. Her durum
değişikliği, kim/ne zaman/hangi durumdan bilgisiyle yazılıyor. Outbox bunun yanına doğal
olarak oturuyor — aynı `SaveChanges` çağrısına üçüncü bir `Add` eklemek yeterli. Servis
katmanı zaten tek `SaveChanges` ile çalışıyor (kodda yorumu var), yani atomiklik hazır.

**Teslim garantisi:** Outbox **en az bir kez** (at-least-once) teslim eder — dispatcher
mesajı gönderip `ProcessedAt` yazmadan crash olursa mesaj tekrar gönderilir. Bu yüzden
**tüketiciler idempotent olmak zorunda** (bölüm 8). "Tam olarak bir kez" (exactly-once)
dağıtık sistemlerde pratikte mümkün değildir; doğru yaklaşım en-az-bir-kez + idempotent tüketici.

---

## 8. Adım 6 — Idempotency

**Tetikleyici:** Queue ile birlikte. İki ayrı ihtiyaç var.

### İhtiyaç 1: Mobil istemci

Gerçek senaryo: Teknisyen sahada, zayıf şebeke. "Tamamlandı" butonuna basıyor, ekran donuyor,
tekrar basıyor. İki `POST /status` isteği gidiyor.

Bugün ne olur? State machine bizi kısmen koruyor: ilk istek `InProgress → Completed` yapar,
ikincisi `Completed → Completed` geçişini `INVALID_STATUS_TRANSITION` (409) ile reddeder.
**Şanslıyız** — ama bu tasarlanmış bir koruma değil, yan etki. Kullanıcı hata mesajı görür,
oysa işlemi başarıyla yapmıştır.

Yorum ekleme endpoint'inde ise koruma **yok**: iki kez basılırsa aynı yorum iki kez yazılır.

**Çözüm:**

```http
POST /api/tickets/{id}/comments
Idempotency-Key: 7c9e6679-7425-40de-944b-e07fc1f90ae7
```

İstemci her kullanıcı eylemi için bir UUID üretir (retry'da **aynı** UUID). Sunucu:
**Hangi endpoint'lerde şart:** `POST /tickets` (çift ticket!), `POST /comments`,
`POST /attachments`. `PUT` ve durum geçişleri doğaları gereği daha korunaklı ama tutarlılık
için hepsine uygulanmalı.

**Neden Redis?** Çok instance'ta in-memory sözlük işe yaramaz — ikinci istek başka instance'a
düşebilir.

### İhtiyaç 2: Queue tüketicileri

Outbox at-least-once teslim ettiği için, "teknisyene bildirim gönder" mesajı iki kez gelebilir.
Tüketici mesaj ID'sini işlenmiş olarak kaydetmeli ve tekrarını atlamalı. Aksi halde teknisyen
aynı bildirimi iki kez alır — zararsız görünür ama e-posta/SMS'te maliyet ve güven kaybıdır.

---

## 9. Adım 7 — Veritabanı Ölçekleme

**Tetikleyici:** ~500K satır veya raporların OLTP'yi yavaşlatması.

### Sıra önemli

1. **Index'ler ve sorgu optimizasyonu** (ücretsiz, Adım 0'da yapıldı)
2. **Read replica** — raporlar (`sql/02-reports.sql`) replica'ya yönlendirilir.
   Uygulama tarafı: ayrı bir read-only `DbContext`. Replication lag (~saniyeler) raporlar
   için sorun değil, **OLTP okumaları için değil** — teknisyen ekranı primary'den okur.
3. **PgBouncer** — yatay ölçekleme sırasında bağlantı çarpanını kesmek için (D3).
4. **Partitioning** — `ticket_status_histories` 6 milyon satıra çıkınca aylık partition
   (`ChangedAt` üzerinden). Eski partition'lar soğuk depoya taşınır.
5. **Arşivleme** — 2 yıldan eski Closed ticket'lar ayrı tabloya/depoya. Operasyonel tablo
   küçük kalır. (Bu, `sql/03-indexes.md`'de reddettiğimiz partial index'i **anlamlı kılar**:
   aktif kayıt oranı %73'ten %20'ye düşünce planlayıcının kararı değişir. Reddedilen bir
   önerinin ne zaman geçerli olacağını bilmek de kararın parçası.)

**Sharding yok.** 1 milyon ticket, tek PostgreSQL instance için mütevazı bir hacimdir.
Sharding'in operasyonel maliyeti (cross-shard sorgu, rebalancing, dağıtık transaction)
bu ölçekte kazancından kat kat fazladır.

---

## 10. Ne Yapmayacağız (ve Neden)

Mimaride "eklemek" kolaydır; "gerekmiyor" diyebilmek zordur ve daha değerlidir.

| Yaklaşım | Neden hayır |
|---|---|
| **Mikroservisler** | 19 RPS'lik bir sistemi 6 servise bölmek: ağ gecikmesi, dağıtık transaction, 6 kat deployment karmaşıklığı, dağıtık debugging. Kazanç: yok. Monolit + yatay ölçekleme bu yükü rahat taşır. Mikroservis **ölçek** sorunu değil, **organizasyon** sorunu çözer (bağımsız ekipler, bağımsız deploy). Tek ekip varsa maliyet, fayda değildir. |
| **Event Sourcing** | Zaten `ticket_status_histories` ile denetim izimiz var — ihtiyacın %90'ı bu. Tam event sourcing: projection yönetimi, event versiyonlama, snapshot'lar. Gerekçe yok. |
| **CQRS (ayrı yazma/okuma modeli)** | Kısmen zaten yapıyoruz: `TicketService` (yazma) / `TicketQueryService` (okuma, `AsNoTracking` + projection). Ayrı veritabanı + eventual consistency **gerekmez**; read replica aynı faydanın büyük kısmını çok daha ucuza verir. |
| **Kafka** | Günde 7.000 mesaj için event streaming platformu. Hangfire yeter. |
| **GraphQL** | Mobil istemcinin ihtiyacı sabit ve bilinen; REST + `allowedNextStatuses` gibi hesaplanmış alanlar yeterli. |
| **Kendi cache katmanımızı yazmak** | Redis + `IDistributedCache` var. |

**İlke:** Her ek bileşen bir operasyonel yük (izleme, yedekleme, sürüm yükseltme, arıza modu).
Ölçülmüş bir sorunu çözmüyorsa eklenmemeli. Bu doküman boyunca her öneriyi bir **tetikleyiciye**
bağladım — "şu olursa şunu yap" — çünkü zamanından önce yapılan optimizasyon, yapılmayan
optimizasyondan pahalıdır.

---

## 11. Hedef Mimari (Adım 6 sonrası)
---

## 12. Özet

| Konu | Karar | Tetikleyici |
|---|---|---|
| **Arama (pg_trgm)** | Denormalize kolon + GIN index | 100K satır — **ölçülmüş zayıflık** |
| **Yatay ölçekleme** | 2+ stateless instance, migration pipeline'a | Hemen (erişilebilirlik) |
| **Logging** | Serilog, structured, correlation ID | Hemen |
| **Monitoring** | OpenTelemetry + **iş metrikleri** (SLA ihlal oranı) | Hemen |
| **Redis** | Referans veri cache + idempotency deposu; ticket verisi **cache'lenmez** | RPS > 50 |
| **Queue** | Hangfire (PostgreSQL storage), broker değil | Bildirim özelliği |
| **Outbox** | Atama → bildirim kaybını önler; mevcut history yapısına oturur | Queue ile |
| **Idempotency** | `Idempotency-Key` + Redis; mobil retry + at-least-once tüketici | Queue ile |
| **Read replica** | Raporlar OLTP'den ayrılır | 500K satır |
| **Mikroservis** | **Hayır** — 19 RPS için maliyet, fayda değil | — |

**Temel tez:** 20.000 DAU bu sistem için korkutucu bir sayı değil (~19 RPS). Asıl tehdit
veri büyümesi (3 yılda 250×) ve ölçülmüş tam tarama sorgusu. Doğru mimari cevabı "her şeyi
ekle" değil, **darboğazı ölç, sırayla çöz, gerekmeyeni ekleme**.
