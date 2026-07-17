## Case Bölümleri

| Bölüm | Nerede |
|---|---|
| 1 — Backend | `TeknikServis.Api/`, `TeknikServis.Application/` · Swagger: localhost:5062/swagger |
| 2 — İş Kuralları | `TeknikServis.Application/Domain/TicketStatusStateMachine.cs` |
| 3 — PostgreSQL | `sql/` klasörü ([README](sql/README.md)) |
| 4 — React Native | `mobile/` |
| 5 — Kod İncelemesi | Bu dosyada ↓ (incelenen kod: `docs/ticketservice.cs`) |
| 6 — AI Kullanım Raporu | Bu dosyada ↓ |
| 7 — Mimari | Bu dosyada ↓ |
| 8 — Production Incident | Bu dosyada ↓ |# Teknik Servis Yönetim Sistemi


### 1. Sistemi ayağa kaldır
```bash
docker compose up -d --build
```
Üç servis kalkar: PostgreSQL (5433), API (5062), pgAdmin (5050).
API, PostgreSQL sağlıklı olana kadar bekler ve ilk açılışta şemayı otomatik kurar.

> **Not:** Şema kurulur ama **veri gelmez** — bir sonraki adım şart.

### 2. Test verisi bas (~4.000 kayıt)
```powershell
Get-Content "sql/01-seed.sql" -Raw | docker exec -i teknikservis-postgres psql -U teknikservis -d teknikservis_db
Get-Content "sql/04-indexes.sql" -Raw | docker exec -i teknikservis-postgres psql -U teknikservis -d teknikservis_db
```
Linux/macOS için: `cat sql/01-seed.sql | docker exec -i ...`

Üretilenler: 4.000 ticket, ~13.600 durum geçmişi, ~5.800 yorum, ~2.800 ek.
`setseed(0.42)` sayesinde dağılımlar her çalıştırmada aynı — `sql/03-indexes.md`'deki ölçümler yeniden üretilebilir.

### 3. Doğrula
| Ne | Nerede |
|---|---|
| Swagger | http://localhost:5062/swagger |
| Sağlık kontrolü | http://localhost:5062/health/db |
| pgAdmin | http://localhost:5050 — `admin@teknikservis.com` / `admin123`, sunucu host: `postgres` |

```powershell
# Uctan uca test (50 senaryo, calisan container'a karsi)
.\scripts\api-smoke-test.ps1

# Birim testler (53 test)
dotnet test TeknikServis.Tests
```

### 4. Raporları çalıştır
```powershell
Get-Content "sql/02-reports.sql" -Raw | docker exec -i teknikservis-postgres psql -U teknikservis -d teknikservis_db -P pager=off
```
Detay ve çalıştırma sırası: [`sql/README.md`](sql/README.md)

### 5. Mobil uygulama (opsiyonel)
```powershell
cd mobile
npm install
npx expo start
```
Telefonda **Expo Go** ile QR'ı okut.

> ⚠️ **Önce `mobile/src/config.js` içindeki `API_URL`'i kendi yerel IP'nizle güncelleyin.**
> Telefon `localhost`'u kendisi olarak görür. IP'nizi öğrenmek için: `ipconfig` (Windows) / `ifconfig` (macOS/Linux).
> Telefon ve bilgisayar aynı ağda olmalı; Windows Firewall 5062 portuna izin vermeli.

### Sıfırdan başlamak
```bash
docker compose down -v    # volume dahil her sey silinir
```

### Kullanılan portlar
5062 (API), 5433 (PostgreSQL), 5050 (pgAdmin). Doluysa `docker-compose.yml`'den değiştirin.




Bölüm 5 : (DOCS dosyası içerisindeki ticketservice incelemesi.)

1. Performans ve Ölçekleme Hataları 

gettickets içinde Tolist en başta kullanılmış, hepsini rame yüklemiş oluyor. sunucu belleğini kapasitesi doldurur.

gettickets'ın foreach döngüsünde her seferinde customer technicians ve history sorgusu atıyor gereksiz yere. 

AssignTechnician senkronken içinde .Result çağrılmış, thread pool starvation'a senep olur.


2. Güvenlik ve transaction hataları

GetTickets içerisinde sortby parametresi dışarıdan müdaheleye açık durumunda.

CreateTicket  ve ChangeStatus  metodlarında birden fazla kez savechanges çağrılmış. UOW kullanımı makul olandır.

CreateTicket içerisinde ServiceTickets.Count() +1 olarak yazılmış eşzamanlı isteklerde mükerrerlik oluşabilir.


3. Validaiton Hataları

ChangeStatus try catch'inde catch kısmı yazılmamış Büyük sıkıntı. yakalicağın bişi yoksa o zaman try-catch'e de gerek yoktur zaten.

4. Kod Kalitesi

- `DateTime.Now` kullanılmış — sunucu saatine bağımlı. Container UTC, geliştirici makinesi UTC+3 → SLA hesabı 3 saat kayar. 
- `throw new Exception("Gecersiz gecis")` — tip yok, middleware hangi HTTP kodunu döneceğini bilemez. 
- Entity döndürülüyor, DTO yok — iç model API sözleşmesi oluyor, yeni alan eklenince istemeden dışarı sızıyor. 
- `newStatus` string, magic string karşılaştırması. 
- Metotlar senkron, EF'ün senkron API'si kullanılmış. 


------------------------------
## BÖLÜM 6 - AI KULLANIM RAPORU (AI USAGE REPORT)
## 1. Kullanılan AI Araçları

Claude: PostgreSQL optimizasyon teorileri, mimari konseptler ve karmaşık hata analizi (debugging) için, sohbet üzerinden adım adım geliştirme, PowerShell betikleri ürettirme, üretilen çıktıyı ölçerek doğrulama.

------------------------------
## 2. En Faydalı Promptlar

   1. Veri Modeli İçin:
   
   " .NET 9 ve EF Core kullanarak ServiceTicket ve TicketStatusHistory arasında 1-to-many ilişki kur. EF Core Fluent API konfigürasyonunu yaz." (TicketStatusHistoryConfiguration.cs)
   
   
   2. State Machine İçin:
   
   "New -> Assigned -> InProgress -> Completed -> Approved -> Closed akışını bozmayacak, geriye dönüşe izin vermeyen .NET 9 Clean Architecture uyumlu Domain Validation kurallarını yaz."
   
   3. PostgreSQL Raporu İçin:
   
   "PostgreSQL'de service_tickets tablosundaki SLA ihlallerini hesaplayan, window fonksiyonu ve CTE içeren performans raporu sorgusunu hazırla."
   
   
   4. Dockerization İçin:
   
   ".NET 9 Web API ve PostgreSQL barındıran, veritabanı ayağa kalkmadan API'nin başlamasını engelleyen (pg_isready kontrollü) docker-compose.yml dosyasını oluştur."
   
   5. Kapsam Denetimi:
   
   "Case'in Bölüm 1 maddelerini tek tek işaretle: hangi tablo istendi, hangisi şemada var ama endpoint'i yok? Kod çalışıyor olması istenen her şeyin yapıldığı anlamına gelmiyor — envanter çıkar."
   
   6. Hata Çözümü (EF Core) İçin:
   
   "DbUpdateConcurrencyException hatası alıyorum. EnableSensitiveDataLogging açıkken loglanan SQL ve takip edilen entity grafiği şu şekilde: [Log_Çıktısı]. Kök nedeni bul."
   
   7. React Native Listeleme İçin:
   
   "React Native (Expo SDK 54) kullanarak, verileri sonsuz kaydırma (Infinite Scroll / Pagination) ile çeken ve performanslı render eden bir FlatList bileşeni yaz."
   
   8. Seed Data Gerçekçiliği İçin:
   
   "PostgreSQL için üreteceğin seed verisinde, servis kayıtlarının açılış tarihlerini rastgele değil, durumlarına (Status) göre son 30 güne mantıklı şekilde dağıt."
   
   9. Test Stratejisi İçin: 
   "Bu servisin birim testlerini InMemory sağlayıcıyla yaz: durum akışının tüm geçerli 
   geçişleri + her kural ihlali ayrı test olsun. Hangi senaryoyu atladığımı da söyle."
   
   10. Uçtan Uca Doğrulama İçin:
   "Çalışan container'daki API'ye karşı koşacak bir PowerShell smoke test yaz: mutlu yol, 
   kural ihlalleri (her biri doğru errorCode ile), sayfalama/filtre/sıralama sınır değerleri. 
   Testler yeşil ama üretimde çalışmıyor olma ihtimalini nasıl kaparım?"
   
   
   
------------------------------
## 3. Reddedilen AI Önerileri ve Düzeltilen AI Hataları
AI tarafından üretilen, kod incelemesi ve testler sırasında fark edilerek tarafımdan reddedilen/düzeltilen kritik vakalar:
## Hata 1: .NET 10 Paket Sürüm Çelişkisi

* Sorun: AI, .NET 9 projesine en güncel NuGet paketlerini (10.x) eklemeyi önerdi.
* Çözüm: Runtime uyuşmazlığını önlemek için paketleri 9.x sürümlerine sabitledim.

## Hata 2: Geçersiz PostgreSQL xmin Concurrency Önerisi

* Sorun: AI, optimistic lock için PostgreSQL'in rezerve sistem kolonu olan xmin'i fiziksel kolon olarak ekleyen hatalı bir EF Core migration'ı üretti (Npgsql 7.0+ ile bu yöntem kaldırılmıştır).
* Çözüm: Migration'ı basmadan önce yakaladım. Guid tabanlı ConcurrencyStamp'e geçtim ama o da işlevsiz çıktı — mekanizmayı tamamen kaldırdım. Case concurrency istemiyor; yarım uygulanmış bir koruma, hiç olmamasından kötüdür.

## Hata 3: İşlevsiz ConcurrencyStamp Güvenlik Katmanı

* Sorun: AI, API'nin istemciye hiçbir zaman stamp değerini dönmediği ve istemciden de doğrulamadığı, sadece mikrosaniyelik pencereleri koruyan ama endpoint'i kıran sahte bir güvenlik katmanı önerdi.
* Çözüm: Koruma sanrısı yaratan bu gereksiz mimari karmaşayı reddettim.

## Hata 4: PowerShell Tırnaklama (Here-String) Çakışması

* Sorun: AI, çok satırlı SQL betiğini psql -c parametresine PowerShell üzerinden aktarmaya çalıştı ve tırnak yapısı (unterminated quoted identifier) bozuldu.
* Çözüm: SQL'i geçici bir dosyaya yazıp stdin üzerinden borulama (pipe) yaparak güvenli şekilde çalıştırdım.

## Hata 5: Ölçülmemiş / Geçersiz Partial Index Önerisi

* Sorun: AI, Sorgu 3'ü hızlandıracağını iddia ederek bir partial index önerdi. Ancak sorguda window function olduğundan ve seçicilik %36 kaldığından query planner index'i tamamen yok saydı.
* Çözüm: İndeksi tabloya göre değil, sorgu şekline göre tasarlamak gerektiğini bilerek ölçüm yaptım ve bu verimsiz öneriyi reddettim.

## Hata 6: Mantıksız Seed Verisi Üretimi (SLA Sapması) ⭐

* Sorun: AI sözdizimsel olarak hatasız ama mantıksal olarak saçma bir veri üretti. 4 saatlik SLA süresi olan işlere 90 saatlik süreler atandığı için SLA ihlal oranı %99.8 çıktı.
* Çözüm: Gecikmeleri SLA sürelerine orantılı, kayıt yaşlarını ise statüye bağlı (4 aydır atanmamış kritik kayıt olmayacak şekilde) revize ettim; oranı gerçekçi bir seviyeye (%37) çektim.

## Hata 7: Middleware Catch Bloklarında Tip Sıralaması

* Sorun: AI, global exception handler içinde DbUpdateConcurrencyException yakalama bloğunu, onun türediği üst tip olan DbUpdateException'ın altına yazdı. Bu yüzden concurrency hataları yanlış yakalanıyordu.
* Çözüm: Spesifik (türetilmiş) exception tipini en üste alarak doğru HTTP hata kodunun dönmesini sağladım.


## Hata 8: Gereksiz index
* Sorun: Ai migration dosyasında gereksiz indexler oluşturmuştu.
* Çözüm: İndexlerin kaç defa kullanıldığına query ile baktım, 0 olanları kaldırdım.




Bölüm 7 MİMARİ ÖLÇEKLENDİRME (20.000 DAU)

Sistemin günlük 20.000 aktif kullanıcıya (DAU) ulaşması durumunda veritabanı yükünü azaltmak, sistem tutarlılığını korumak ve kesintisiz çalışmayı sağlamak için mimari dönüşüm ve bileşenler şunlardır:

1. Veri Okuma Yükünün Azaltılması (Redis)

Aksiyon: Sık değişmeyen ve her istekte veritabanına yük bindiren verileri (Teknisyen/müşteri listeleri, ticket durumları vb.) Redis üzerinde önbelleğe (Cache) alınır.

Strateji: Cache-Aside deseni uygulanır. Veri güncellendiğinde (örneğin yeni teknisyen eklendiğinde) Redis key'i otomatik geçersiz kılınır (Invalidation).

2. Asenkron İşlemler & Darboğaz Yönetimi (Queue & Background Job)

Aksiyon: Anlık cevap gerektirmeyen, API'yi yavaşlatan işlemleri ana akıştan koparmak.

Strateji: Mesaj kuyruğu olarak RabbitMQ, arka plan işleyicisi (Worker) olarak ise .NET 9 BackgroundService kullanırım. API mesajı kuyruğa atar ve anında Accepted döner; işlem arka planda sırayla tüketilir.

3. Veri Tutarlılığı & Güvenilirlik (Transactional Outbox Pattern)

Aksiyon: Bir ticket durumu değiştiğinde hem DB'ye yazıp hem de kuyruğa mesaj atarken yaşanabilecek ağ kesintisi riskini (Veritabanı güncellendi ama mesaj gitmedi durumu) engellemek.

Strateji: Durum değişikliği ve kuyruk mesajı, PostgreSQL içinde aynı transaction ile tek bir Outbox tablosuna yazılır. 
Ayrı bir arka plan servisi bu tabloyu okuyarak mesajları RabbitMQ'ya güvenli bir şekilde (At-least-once delivery) ulaştırır.

4. Mükerrer İşlem Engelleme (Idempotency)

Aksiyon: Mobil uygulamadaki teknisyen zayıf internet nedeniyle "Tamamla" butonuna üst üste bastığında veya kuyruk mekanizması aynı mesajı tekrar işlediğinde verinin bozulmasını önlenir.

Strateji: Her kritik isteğe (örn: ticket oluşturma/durum değiştirme) istemci tarafından benzersiz bir Idempotency-Key (UUID) atanır. 
API, bu key'i Redis veya DB'de kontrol eder; işlem zaten yapılmışsa tekrar çalıştırmadan doğrudan eski sonucu döner.
(veyahut bir kere butona basıldığında butonu anında inaktif ederiz :)))) )

5. Sistem Takibi (Logging & Monitoring)

Aksiyon: 20.000 kullanıcıda kör uçuş yapmamak için sistemin sağlık durumunu ve hata metriklerini merkezi hale getirilir.

Strateji (Logging): Serilog ile JSON formatında yapılandırılmış loglar (Structured Logging) üretirim. Loglar OpenSearch veya Axiom gibi merkezi bir panele akar.

Strateji (Monitoring): Uygulama metriklerini (CPU, RAM, request/response süreleri) .NET 9 OpenTelemetry desteği ile toplayıp Prometheus ve Grafana panellerinde görselleştiririm.




Bölüm 8 PRODUCTION INCIDENT PLAN


Projede CPU, timeout ve deadlock risklerini önceden öngörerek mimariyi buna göre kurulması gerekir:

Önleyici Mimari: Tüm veritabanı işlemlerinde async/await kullanırdım. Result veya Wait() gibi thread kilitleyen (blocking) yapılardan kaçınırdım.

Gözlemlenebilirlik: Serilog ile yapılandırılmış loglar (Structured Logging) yazarım. Her isteğe bir CorrelationId atayarak timeout ve kilitlenmelerin tam kaynağını (hangi endpoint/query) tespit etmeyi kolaylaştırdım.

Olası bir kriz anında izleyeceğim 3 adımlı yol haritam ve AI destek planım şudur:

1. CPU Tavan Yaparsa (High CPU)

Aksiyonum: Canlı ortamda dotnet-trace ve dotnet-dump araçlarıyla hızlıca bir dump alırım. Hangi metotların veya sonsuz döngülerin CPU'yu tükettiğini bulurum.

AI Desteği (Prompt):

"Aşağıdaki .NET 9 dump analiz çıktısında en yüksek CPU tüketen thread'lerin çağrı yığını (call stack) yer alıyor. Buradaki darboğazı (bottleneck) tespit et ve kodu optimize et: [DUMP_ÇIKTISI_VEYA_STACK_TRACE]"

2. Zaman Aşımı Yaşanırsa (Timeout)

Aksiyonum: Logları incelerim. Sorun veritabanı kaynaklıysa PostgreSQL pg_stat_activity ile uzun süren sorguları ve eksik indeksleri yakalarım.

AI Desteği (Prompt):

"PostgreSQL'de 5 saniyeden uzun süren ve timeout'a neden olan şu sorgu için execution plan çıktısı aşağıdadır. Hangi indeksi eklemeliyim veya sorguyu nasıl optimize etmeliyim? [SQL_VE_EXPLAIN_ÇIKTISI]"

3. Deadlock Olursa

Aksiyonum: PostgreSQL loglarından birbirini kilitleyen Process A ve Process B sorgularını çekerim. Kod tarafında aynı lock objesini farklı sıra ile çağıran metotları izole ederim.

AI Desteği (Prompt):

"Şu iki API endpoint'i eşzamanlı çalıştığında PostgreSQL'de deadlock oluşuyor. İşlem sırasını düzeltmek veya SELECT FOR UPDATE / SKIP LOCKED kullanmak için bu iki metodu nasıl revize etmeliyim? [KOD_BLOKLARI]"
