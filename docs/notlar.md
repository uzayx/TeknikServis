# Proje Notları (çalışma dosyası)

> Bu dosya README ve AI Usage Report'un ham malzemesidir. Proje boyunca güncellenir,
> sonunda derlenip `README.md` + `docs/ai-usage-report.md` haline getirilir.

---

## 1. AI Kullanım Vakaları (Bölüm 6)

Kalıp: **Hata → Tespit → Çözüm → Ders**

### V1 — .NET 10 paket sürüm uyuşmazlığı
Proje .NET 9 ile kurulmuşken NuGet varsayılan olarak .NET 10 paketlerini çekti.
Paketleri 9.x'e sabitleyerek çözdüm.

### V2 — `xmin` concurrency: obsolete API + fiziksel kolon
AI, PostgreSQL'in `xmin` sistem kolonunu concurrency token olarak önerdi ve elle kolon
eşlemesi yaptı. Migration'ı **basmadan önce okudum**: `service_tickets` tablosuna fiziksel
bir `xmin` kolonu eklenmeye çalışılıyordu — `xmin` rezerve sistem kolonu, script veritabanı
tarafından reddedilecekti. Npgsql dokümanına baktım: 7.0'dan itibaren `UseXminAsConcurrencyToken`
kaldırılmış, yerine `IsRowVersion()` gelmiş. Onu denedim — migration hâlâ fiziksel kolon üretti
(provider'da bilinen tekrar eden davranış).
**Ders:** Migration'ı okumadan basmak, hatayı deployment anına taşır.

### V3 — `ConcurrencyStamp`: gereksiz savunma katmanının maliyeti
`xmin` yerine Guid tabanlı stamp'e geçtim. Atama endpoint'i sebepsiz `CONCURRENCY_CONFLICT`
vermeye başladı. İlk teorim "sistem beni koruyor" oldu; akışı takip edince yanlış olduğunu
gördüm — hatalı istek zaten `SaveChanges`'e ulaşmamıştı ve her istek kendi DbContext'iyle
veriyi taze okuyordu.
Daha derine inince mekanizmanın **zaten işlevsiz** olduğunu fark ettim: optimistic concurrency'nin
çalışması için istemcinin okuduğu versiyonu geri göndermesi gerekir; API'miz stamp'i ne
döndürüyor ne kabul ediyordu. Tek istek içindeki mikrosaniyelik pencereyi koruyordu.
**Karar:** Katmanı kaldırdım — (1) gereksinim değildi, (2) doğru uygulanmadığı için koruma
sağlamıyordu, (3) çalışan endpoint'i kırıyordu.
**Ders:** Doğru uygulanmamış bir güvenlik katmanı, koruma sanrısı yaratır.

### V4 — EF change tracking: INSERT yerine UPDATE ⭐ (asıl bug)
409 hatası `ConcurrencyStamp` kaldırıldıktan sonra da sürdü. Sırasıyla üç şeyi suçladık,
üçünde de yanıldık: xmin, ConcurrencyStamp, EF paket sürüm uyumsuzluğu.
Kırılma noktası tahmini bırakıp `EnableSensitiveDataLogging` ile gerçek SQL'i okumak oldu:**Kök neden:** Takip edilen bir entity'nin navigation koleksiyonuna, PK'sı elle atanmış yeni
nesne eklemek. EF, `DetectChanges` sırasında dolu key'i görüp kaydı `Modified` sanıyordu.
`CreateAsync` çalışıyordu çünkü `DbSet.Add()` tüm grafiği zorla `Added` yapar.
**Çözüm:** History'yi navigation yerine doğrudan `DbSet`'e eklemek (tek satır).
**Dersler:** (1) Framework'ün hata mesajı semptomu anlatır, nedeni değil — "concurrency"
kelimesine kilitlenmek iki gereksiz refactor'a mal oldu. (2) Hipotez test etmek için kod
değiştirmek pahalı; gözlem aracını açmak ucuz. (3) Testler bug'ı her koşuda gösterdi.

### V5 — PowerShell here-string + `psql -c`
AI, çok satırlı SQL'i `psql -c` parametresine here-string ile geçen komut önerdi.
PowerShell string'i ayrı argümanlara bölünce tırnak yapısı bozuldu
(`unterminated quoted identifier`). Çözüm: SQL'i dosyaya yazıp stdin'den boru etmek —
zaten seed'de çalışan yöntem buydu.
**Ders:** Çalışan bir yöntem varken alternatif üretmek gereksiz risk.

### V6 — Ölçülmemiş index önerisi
AI, partial index'i "Sorgu 3'ü hızlandırır" diyerek önerdi. Ölçtüğümde planlayıcı index'i
tamamen yok saydı: sorguda window function vardı (sıralamayı zaten yok ediyor) ve seçicilik
%36'ydı. Aynı index, window'suz sorguda 5 kat hızlandırdı.
**Reddettiğim öneri:** Index'i ölçmeden dokümana yazmak.
**Ders:** Index tablo için değil, sorgu şekli için tasarlanır.

### V7 — Seed script'i çalıştı ama veri saçmaydı ⭐
AI'ın ürettiği seed sözdizimsel olarak kusursuzdu. Raporları koşunca SLA ihlal oranının
**%99.8** çıktığını gördüm. Sorgu doğruydu, veri saçmaydı: gecikmeler önceliğe bakılmaksızın
sabit aralıktan üretiliyordu (4 saatlik SLA'ya karşı 2-90 saatlik iş süresi → ihlal
matematiksel olarak kaçınılmaz). Ayrıca açık kayıtlar 180 güne yayılmıştı — 4 aydır atanmamış
kritik arıza gerçekçi değil.
**Çözüm:** Gecikmeleri SLA'ya orantılı, kayıt yaşını duruma bağlı hale getirdim.
İhlal oranı %37'ye indi.
**Ders:** "Çalışan script" ile "doğru veri" aynı şey değil. Ancak sorguları gerçekten
çalıştırıp çıktılara baktığım için yakaladım.

### V8 — `switch` ifadesinde tip sırası
Middleware'de `DbUpdateConcurrencyException`, `DbUpdateException`'dan türediği için base tip
önce yazılınca türetilmiş dal asla eşleşmiyordu; concurrency hataları yanlış etiketle
dönüyordu.

### V9 — Kirli ölçüm ortamı ⭐ (metodolojik)
AI'ın hazırladığı EXPLAIN ölçüm betiği, Öneri 1'in "önce" durumunu ölçerken yalnızca ilgili
index'i düşürdü; Öneri 2'nin covering index'i ortamda kaldı. Sonuç: "index yok" diye
etiketlenen plan aslında başka bir index'i kullanıyordu — karşılaştırma baştan geçersizdi.
**Tespit:** Plan çıktısında `Index Only Scan using ix_tickets_sla_covering` yazıyordu, oysa
o adımda hiçbir index olmamalıydı. Sayılara değil plana bakınca fark ettim.
**Çözüm:** Her varyantı izole eden yeni bir betik: A) hiçbir yeni index yok, B) sadece partial,
C) sadece covering, D) ikisi birden.
**Bonus bulgu:** İzole ölçüm, covering index'in partial index'i **gereksiz kıldığını** ortaya
çıkardı — planlayıcı ikisi birden varken partial'a dönüp bakmıyor (cost 3.85 vs 11.30).
Öneri sayısı 3'ten 2'ye indi; partial index "kanıtla reddedildi" olarak dokümante edildi.
**Ders:** A/B ölçümünde tek değişkeni izole etmek şart. "Önce" durumunun gerçekten önce
olduğunu plan çıktısından doğrula — aksi halde ölçtüğün şey senin sandığın şey değildir.

### V10 — Expo SDK sürüm uyarısının hafife alınması
AI, Expo projesini kurarken "Latest (SDK 57)" seçeneğini önerdi; listede açıkça duran
"For learning with Expo Go (SDK 54)" satırını bilgi sanıp geçti. Play Store'daki Expo Go
henüz SDK 57'yi desteklemiyordu, uygulama "Project is incompatible" hatası verdi.
Proje SDK 54 ile yeniden kuruldu, kaynak dosyalar (7 dosya) aynen taşındı — iskelet değişti,
kod değişmedi.
**Ders:** Bir araç, seçeneğin yanına neden açıklama yazıyorsa o açıklama bilgi değil uyarıdır.

### V11 — PowerShell ↔ JavaScript kaçış karakteri çakışması
AI, mevcut bir JS dosyasına satır eklemek için PowerShell `$str.Replace()` kullandı ve
değiştirme metnini **çift tırnak** içine aldı. PowerShell çift tırnaklı string'de backtick'i
kaçış karakteri olarak yorumladığı için JavaScript template literal'ları (`` `...${id}...` ``)
silindi; dosya `Invalid regular expression flag` hatasıyla derlenmedi.
**Çözüm:** Dosyayı tek tırnaklı here-string (`@'...'@`) ile bütün olarak yeniden yazmak —
orada backtick literal kalır.
**Ders:** İki dilin kaçış kuralları çakıştığında (PowerShell ↔ JS), enterpolasyonsuz aktarım
tek güvenli yol. "Küçük bir ekleme" için parçalı düzenleme, dosyayı baştan yazmaktan riskli.

### V12 — Türkçe "I" problemi: kültüre bağımlı ToLower() ⭐
Aramaya müşteri/teknisyen adı eklendikten sonra `"AHMET YILMAZ"` sorgusu **0 sonuç** döndürdü.
**Kök neden:** Geliştirme makinesi Türkçe yerel ayarlarda; .NET'te `ToLower()` işletim
sisteminin kültürünü kullanır. Türkçede büyük `I` küçülünce **noktasız `ı`** olur:
`"YILMAZ".ToLower()` → Türkçe kültürde `"yılmaz"`, Invariant'ta `"yilmaz"`. Veritabanındaki
`"Yilmaz"` (noktalı i) ile eşleşme çöktü.
**Önemli nüans:** Bug'ı InMemory test yakaladı çünkü o sağlayıcı sorguyu istemci tarafında
gerçek .NET string metotlarıyla çalıştırır. Gerçek PostgreSQL'de Npgsql `ToLower()`'ı SQL'in
`LOWER()` fonksiyonuna çevirir ve veritabanı collation'ı devreye girer — yani API muhtemelen
doğru çalışıyordu, test ortamı üretim davranışından sapıyordu.
**Çözüm:** `ToLower()` → `ToLowerInvariant()`.
**Ders:** Metin karşılaştıran kod, çalıştığı sunucunun yerel ayarına asla güvenmemeli.
`Invariant` varyantları varsayılan olmalı, istisna değil. Ayrıca: test ortamının üretimden
farklı davranabileceğini bilmek, testin verdiği sonucu doğru yorumlamak için şart.

### V13 — Sonradan fark edilen kapsam boşluğu (envanter disiplini)
Bölüm 3 bittikten sonra "başka eksik var mı?" diye envanter çıkarınca, `Comment` ve
`Attachment` tablolarının şemada olduğu ama **hiçbir endpoint'inin bulunmadığı** görüldü.
Case Bölüm 1'de bu tabloları açıkça istiyordu; tasarlanmış ama dış dünyaya açılmamışlardı.
Eklendi (yorum + ek metadata endpoint'leri, 8 birim testi, 7 smoke test adımı).
**Ders:** "Bölüm bitti" demeden önce case'in maddelerini tek tek işaretlemek gerekiyor;
kod çalışıyor olması, istenen her şeyin yapıldığı anlamına gelmiyor.

### V14 — Testi düzeltirken üretimi kırmak ⭐
V12'deki Türkçe I problemini çözmek için ToLower() -> ToLowerInvariant() yaptım. Toplu -replace kullandım ve kör davrandı: hem arama terimini (istemci tarafı — doğru) hem de entity property'lerini (SQL'e çevrilmesi gereken — yanlış) değiştirdi. EF Core ToLowerInvariant()'ı SQL'e çeviremez; arama gerçek veritabanında tamamen çöktü.
**Kritik nokta:** 53 birim testi yeşil kaldı. InMemory sağlayıcı sorguyu istemci tarafında gerçek .NET metotlarıyla çalıştırırsa regresyonu göremezdi. Hatayı ancak gerçek Postgres'e karşı koşan uçtan uca smoke test yakaladı (search=kombi daha önce geçen bir kontroldü, kaldı).
**Doğru çözüm:** Arama terimi istemci tarafında ToLowerInvariant() ile normalize edilir; entity property'lerinde ToLower() kalır, EF onu SQL LOWER()'ına çevirir.
**Dersler:** (1) Toplu metin değiştirme, bağlamı olmayan bir araçtır — aynı metot çağrısı iki farklı yerde iki farklı anlam taşıyabilir. (2) Birim testlerinin yeşil olması "çalışıyor" demek değil; test sahtesi (InMemory) üretim davranışını taklit etmiyorsa yanlış güven verir. (3) Gerçek veritabanına karşı koşan bir entegrasyon testi, birim test kalabalığının kaçırdığını yakalar — bu projede tam olarak bu oldu.

---

## 2. En Faydalı Promptlar (Bölüm 6 — 10 tane seçilecek)

1. "Bu migration'ı basmadan önce ne ürettiğini göster, riskli bir şey var mı?"
2. "Bu hatayı çözmek için tahmin yürütme; hangi gözlem aracını açmalıyım?"
3. "Bu index önerisini EXPLAIN ANALYZE ile önce/sonra ölç, planları karşılaştır"
4. "Bu planı oku: hangi erişim yöntemi, sort kalktı mı, buffers kaç?"
5. "Bu mekanizmayı doğru uygulayacak bütçem var mı? Yoksa kaldıralım mı?"
6. "Case'de istenmiyor ama ekleyeyim mi? Fayda/zarar analizi yap"
7. "Bu tasarım kararını mülakatta nasıl savunurum?"
8. "Kendi planını da kontrol et; benim isteğimde yanlış gördüğün var mı?"
9. "Sorguyu yazdık ama çalıştırmadık — çıktı gerçekten anlamlı mı?"
10. (doldurulacak)

---

## 3. Bilinçli Kapsam Dışı (README'ye)

| Konu | Karar | Gerekçe |
|---|---|---|
| Authentication/Authorization | Yok | Case istemiyor |
| Customer/Technician update/delete | Yok | İş kuralı içermiyor, CRUD şablonu puan getirmez |
| Teknisyen deactivate endpoint'i | Yok | Kural birim testiyle doğrulandı |
| Specialty | Serbest metin | Üretimde lookup tablosuna taşınmalı (veri tutarlılığı + uzmanlık bazlı raporlama) |
| Attachment dosya yükleme | Metadata | İstemci S3/blob'a yükler, API yalnızca URL + metadata saklar → API sunucusu dosya trafiğinden ayrışır |
| Raporlar API endpoint'i | Yok | SQL script olarak sunuldu; üretimde read-replica üzerinden ayrı raporlama servisi |
| `pg_trgm` + GIN (arama) | Yok | Bu hacimde ek uzantı bağımlılığına değmez |
| Kolon adlandırma | PascalCase | EF varsayılanı; tablolar snake_case — küçük tutarsızlık, düzeltmek migration'ı baştan yazmayı gerektirir |
| Secret yönetimi | appsettings'te düz metin | Case study; üretimde secret manager / env |

---

## 4. Bölüm 5 (Kod İncelemesi) — örnek servise koyulacak hatalar

Gerçek projede yaşadıklarımızdan:
- Navigation koleksiyonuna PK'lı nesne ekleme → INSERT yerine UPDATE (V4)
- Gereksiz `BeginTransaction` (tek `SaveChanges` zaten transaction)
- `switch` ifadesinde base tipi türetilmişten önce yazmak (V8)
- Covering index'te gerekli kolonu unutmak (V6)
- Kulture bagimli ToLower/ToUpper (V12) -> ToLowerInvariant / StringComparison.OrdinalIgnoreCase

Klasik AI hataları (eklenecek):
- N+1 sorgu (`Include` yerine döngüde sorgu)
- `SortBy` string'ini dinamik SQL'e sokmak → injection
- `AsNoTracking` unutmak (okuma sorgularında)
- Validation'ı controller'da elle yapmak
- `async void`, `.Result`, `.Wait()` deadlock
- Exception yutmak (`catch { }`)
- Hassas veriyi log'a basmak

---

## 5. Yapılacaklar

- [ ] Docker Compose'a API + Dockerfile ekle
- [ ] Bölüm 4: React Native
- [ ] Bölüm 5: Kod incelemesi
- [ ] Bölüm 7: Mimari (20K DAU)
- [ ] Bölüm 8: Production incident
- [ ] README derle
- [ ] AI Usage Report derle



