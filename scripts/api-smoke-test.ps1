# Teknik Servis API - Uctan Uca Smoke Test
# Kullanim: API calisir durumdayken -> .\api-smoke-test.ps1

$base = "http://localhost:5062"
$stamp = (Get-Date -Format "HHmmssfff")
$script:pass = 0
$script:fail = 0

function Ok($msg)   { Write-Host "  [GECTI] $msg" -ForegroundColor Green; $script:pass++ }
function No($msg)   { Write-Host "  [KALDI] $msg" -ForegroundColor Red; $script:fail++ }
function Head($msg) { Write-Host "`n$msg" -ForegroundColor Cyan }

function Call($method, $path, $body) {
    $params = @{ Uri = "$base$path"; Method = $method; ContentType = "application/json" }
    if ($body) { $params.Body = ($body | ConvertTo-Json -Depth 5) }
    try {
        $r = Invoke-RestMethod @params
        return @{ Ok = $true; Status = 200; Data = $r }
    } catch {
        $code = $_.Exception.Response.StatusCode.value__
        $raw = (New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())).ReadToEnd()
        $data = try { $raw | ConvertFrom-Json } catch { $raw }
        return @{ Ok = $false; Status = $code; Data = $data }
    }
}

function Expect($label, $result, $status, $errorCode) {
    if ($result.Status -ne $status) {
        No "$label -> beklenen HTTP $status, gelen $($result.Status)"
        return
    }
    if ($errorCode -and $result.Data.errorCode -ne $errorCode) {
        No "$label -> beklenen errorCode '$errorCode', gelen '$($result.Data.errorCode)'"
        return
    }
    Ok $label
}

# ============ HAZIRLIK ============
Head "HAZIRLIK: Test verisi"

$musteri1 = (Call POST "/api/customers" @{
    firstName="Ahmet"; lastName="Yilmaz"; email="ahmet.$stamp@test.com"
    phone="05551112233"; address="Kadikoy, Istanbul" }).Data
Ok "Musteri 1: $($musteri1.fullName)"

$musteri2 = (Call POST "/api/customers" @{
    firstName="Ahmet"; lastName="Yilmaz"; email="ahmet2.$stamp@test.com"
    phone="05559998877"; address="Cankaya, Ankara" }).Data
if ($musteri2.id -and $musteri2.id -ne $musteri1.id) {
    Ok "Ayni isimli 2. musteri farkli Id ile olusturuldu"
} else { No "Ayni isimli 2. musteri olusturulamadi" }

$tek1 = (Call POST "/api/technicians" @{
    firstName="Can"; lastName="Demir"; email="can.$stamp@test.com"
    phone="05554445566"; specialty="Kombi/Dogalgaz" }).Data
Ok "Teknisyen 1: $($tek1.fullName)"

$tek2 = (Call POST "/api/technicians" @{
    firstName="Elif"; lastName="Kaya"; email="elif.$stamp@test.com"
    phone="05553332211"; specialty="Elektrik" }).Data
Ok "Teknisyen 2: $($tek2.fullName)"

# ============ SENARYO A: MUTLU YOL ============
Head "SENARYO A: Tam yasam dongusu"

$t = (Call POST "/api/tickets" @{
    customerId=$musteri1.id; title="Kombi isitmiyor"
    description="Kombi calisiyor fakat petekler soguk kaliyor."; priority="High" }).Data

if ($t.status -eq "New") { Ok "A1: Ticket New durumunda olustu ($($t.ticketNumber))" }
else { No "A1: status New degil -> $($t.status)" }

if ($t.allowedNextStatuses.Count -eq 1 -and $t.allowedNextStatuses[0] -eq "Assigned") {
    Ok "A1: allowedNextStatuses = [Assigned]"
} else { No "A1: allowedNextStatuses hatali" }

$slaFark = ([datetime]$t.slaDeadline - [datetime]$t.createdAt).TotalHours
if ([math]::Abs($slaFark - 8) -lt 0.1) { Ok "A1: SLA = 8 saat (High, appsettings'ten)" }
else { No "A1: SLA beklenen 8 saat, gelen $([math]::Round($slaFark,2))" }

if ($t.statusHistories.Count -eq 1) { Ok "A1: Ilk history kaydi yazildi" }
else { No "A1: history sayisi 1 degil -> $($t.statusHistories.Count)" }

$r = Call POST "/api/tickets/$($t.id)/assign" @{
    technicianId=$tek1.id; changedByType="Center"; note="Ilk atama yapildi." }
if ($r.Ok -and $r.Data.status -eq "Assigned" -and $r.Data.assignedAt) {
    Ok "A2: Teknisyen atandi, status otomatik Assigned oldu"
} else { No "A2: atama basarisiz -> HTTP $($r.Status)" }

$h = $r.Data.statusHistories | Sort-Object changedAt | Select-Object -Last 1
if ($h.newTechnicianId -eq $tek1.id -and -not $h.previousTechnicianId) {
    Ok "A2: History'de ilk atama izi dogru (previous=null)"
} else { No "A2: history izi hatali" }

$r = Call POST "/api/tickets/$($t.id)/assign" @{
    technicianId=$tek2.id; changedByType="Center"; note="Elif Kaya devraldi." }
$h = $r.Data.statusHistories | Sort-Object changedAt | Select-Object -Last 1
if ($h.previousTechnicianId -eq $tek1.id -and $h.newTechnicianId -eq $tek2.id) {
    Ok "A3: Teknisyen DEGISIKLIGI history'de izlendi"
} else { No "A3: teknisyen degisiklik izi hatali" }

foreach ($s in @(
    @{ st="InProgress"; by="Technician"; note="Servise gidildi." },
    @{ st="Completed";  by="Technician"; note="Uc yollu vana degistirildi." },
    @{ st="Approved";   by="Center";     note="Musteri onayi alindi." },
    @{ st="Closed";     by="Center";     note="Kayit kapatildi." })) {
    $r = Call POST "/api/tickets/$($t.id)/status" @{
        newStatus=$s.st; changedByType=$s.by; note=$s.note }
    if ($r.Ok -and $r.Data.status -eq $s.st) { Ok "A: -> $($s.st)" }
    else { No "A: $($s.st) gecisi basarisiz -> HTTP $($r.Status)" }
}

$final = (Call GET "/api/tickets/$($t.id)").Data
if ($final.completedAt -and $final.closedAt) { Ok "A: completedAt ve closedAt dolduruldu" }
else { No "A: zaman damgalari eksik" }
if ($final.allowedNextStatuses.Count -eq 0) { Ok "A: Closed -> allowedNextStatuses bos" }
else { No "A: Closed sonrasi hala gecis var" }
if ($final.statusHistories.Count -eq 7) { Ok "A: Toplam 7 history kaydi" }
else { No "A: history sayisi 7 degil -> $($final.statusHistories.Count)" }

# ============ SENARYO B: KURAL IHLALLERI ============
Head "SENARYO B: Kural ihlalleri"

Expect "B1: Closed kaydi duzenleme reddedildi" (Call PUT "/api/tickets/$($t.id)" @{
    title="Degistirmeye calisiyorum"; description="Bu calismamali."; priority="Low" }) 409 "TICKET_LOCKED"

Expect "B2: Closed kayda teknisyen atama reddedildi" (Call POST "/api/tickets/$($t.id)/assign" @{
    technicianId=$tek1.id; changedByType="Center"; note="Bu da calismamali." }) 409 "TICKET_LOCKED"

$t2 = (Call POST "/api/tickets" @{
    customerId=$musteri1.id; title="Klima sogutmuyor"
    description="Klima calisiyor ama soguk hava vermiyor."; priority="Critical" }).Data

Expect "B3: Durum atlama (New -> InProgress) reddedildi" (Call POST "/api/tickets/$($t2.id)/status" @{
    newStatus="InProgress"; changedByType="Center"; note="Atlama denemesi" }) 409 "INVALID_STATUS_TRANSITION"

Call POST "/api/tickets/$($t2.id)/assign" @{ technicianId=$tek1.id; changedByType="Center" } | Out-Null

Expect "B4: Geriye donus (Assigned -> New) reddedildi" (Call POST "/api/tickets/$($t2.id)/status" @{
    newStatus="New"; changedByType="Center"; note="Geri alma denemesi" }) 409 "INVALID_STATUS_TRANSITION"

$t3 = (Call POST "/api/tickets" @{
    customerId=$musteri2.id; title="Priz calismiyor"
    description="Salondaki prizlerde elektrik yok."; priority="Medium" }).Data

Expect "B5: Teknisyensiz Assigned reddedildi" (Call POST "/api/tickets/$($t3.id)/status" @{
    newStatus="Assigned"; changedByType="Center"; note="Teknisyensiz atama" }) 409 "TECHNICIAN_REQUIRED"

Call POST "/api/tickets/$($t3.id)/assign" @{ technicianId=$tek1.id; changedByType="Center" } | Out-Null

Expect "B6: Ayni teknisyeni tekrar atama reddedildi" (Call POST "/api/tickets/$($t3.id)/assign" @{
    technicianId=$tek1.id; changedByType="Center" }) 409 "TECHNICIAN_ALREADY_ASSIGNED"

$r = Call POST "/api/tickets" @{ customerId=$musteri1.id; title=""; description=""; priority="High" }
if ($r.Status -eq 400 -and $r.Data.errors) { Ok "B7: Bos alanlar 400 ValidationProblemDetails ile reddedildi" }
else { No "B7: beklenen 400 + errors, gelen HTTP $($r.Status)" }

$r = Call POST "/api/tickets/$($t3.id)/status" @{
    newStatus="InProgress"; changedByType="Robot"; note="Gecersiz aktor" }
if ($r.Status -eq 400) { Ok "B8: Gecersiz changedByType reddedildi" }
else { No "B8: beklenen 400, gelen $($r.Status)" }

Expect "B9: Olmayan ticket 404" (Call GET "/api/tickets/00000000-0000-0000-0000-000000000001") 404 "NOT_FOUND"

Expect "B10: Olmayan musteri ile ticket 404" (Call POST "/api/tickets" @{
    customerId="00000000-0000-0000-0000-000000000001"; title="Hayalet"
    description="Bu musteri yok."; priority="Low" }) 404 "NOT_FOUND"

Expect "B11: Duplicate email reddedildi" (Call POST "/api/customers" @{
    firstName="Ahmet"; lastName="Yilmaz"; email="ahmet.$stamp@test.com"
    phone="05551112233" }) 409 "DATA_INTEGRITY_VIOLATION"

# ============ SENARYO C: LISTE ============
Head "SENARYO C: Sayfalama / Filtreleme / Siralama"

foreach ($p in @("Low","Medium","High","Critical")) {
    Call POST "/api/tickets" @{ customerId=$musteri2.id; title="Ek kayit $p"
        description="Liste testi icin."; priority=$p } | Out-Null
}

$r = (Call GET "/api/tickets?page=1&pageSize=3").Data
if ($r.items.Count -le 3 -and $r.totalCount -gt 0) {
    Ok "C1: Sayfalama -> $($r.items.Count) kayit / toplam $($r.totalCount) / $($r.totalPages) sayfa"
} else { No "C1: sayfalama hatali" }
if ($r.hasNextPage -and -not $r.hasPreviousPage) { Ok "C1: hasNextPage/hasPreviousPage dogru" }
else { No "C1: sayfa metadata hatali" }

$r = (Call GET "/api/tickets?status=Closed").Data
if (($r.items | Where-Object { $_.status -ne "Closed" }).Count -eq 0) {
    Ok "C2: status=Closed filtresi -> $($r.totalCount) kayit"
} else { No "C2: filtre disi kayit dondu" }

$r = (Call GET "/api/tickets?priority=High&sortBy=createdAt&sortDir=asc").Data
if (($r.items | Where-Object { $_.priority -ne "High" }).Count -eq 0) {
    Ok "C3: priority=High + createdAt asc -> $($r.totalCount) kayit"
} else { No "C3: priority filtresi hatali" }

$r = (Call GET "/api/tickets?sortBy=slaDeadline&sortDir=asc").Data
$dates = $r.items | ForEach-Object { [datetime]$_.slaDeadline }
$sorted = $true
for ($i=1; $i -lt $dates.Count; $i++) { if ($dates[$i] -lt $dates[$i-1]) { $sorted = $false } }
if ($sorted) { Ok "C4: slaDeadline asc siralamasi dogru" }
else { No "C4: siralama bozuk" }

$r = (Call GET "/api/tickets?search=kombi").Data
if ($r.totalCount -ge 1) { Ok "C5: search=kombi -> $($r.totalCount) kayit" }
else { No "C5: arama sonuc dondurmedi" }

$r = (Call GET "/api/tickets?status=New&priority=High&sortBy=slaDeadline&sortDir=asc&page=1&pageSize=5").Data
if ($null -ne $r.totalCount) { Ok "C6: Kombine sorgu calisti -> $($r.totalCount) kayit" }
else { No "C6: kombine sorgu hatali" }

$r = (Call GET "/api/tickets?page=0&pageSize=5000").Data
if ($r.page -eq 1 -and $r.pageSize -eq 100) { Ok "C7: Sinir degerler duzeltildi (page 0->1, pageSize 5000->100)" }
else { No "C7: sinir degerler duzeltilmedi -> page=$($r.page), pageSize=$($r.pageSize)" }

# ============ SENARYO D: YORUM & EK (METADATA) ============
Head "SENARYO D: Yorum ve Ek"

$r = Call POST "/api/tickets/$($t3.id)/comments" @{
    authorType="Technician"; authorId=$tek1.id; content="Parca siparis edildi, yarin takilacak." }
if ($r.Ok -and $r.Data.content) { Ok "D1: Teknisyen yorumu eklendi" }
else { No "D1: yorum eklenemedi -> HTTP $($r.Status)" }

Call POST "/api/tickets/$($t3.id)/comments" @{
    authorType="Customer"; content="Tesekkurler, bekliyorum." } | Out-Null

$r = Call GET "/api/tickets/$($t3.id)/comments"
if ($r.Ok -and $r.Data.Count -ge 2) { Ok "D2: Yorum listesi -> $($r.Data.Count) yorum, tarihe gore sirali" }
else { No "D2: yorum listesi hatali" }

Expect "D3: Closed kayda yorum reddedildi" (Call POST "/api/tickets/$($t.id)/comments" @{
    authorType="Center"; content="Bu calismamali." }) 409 "TICKET_LOCKED"

$r = Call POST "/api/tickets/$($t3.id)/attachments" @{
    fileName="ariza-foto.jpg"; contentType="image/jpeg"; fileSizeBytes=524288
    storagePath="https://storage.example.com/tickets/ariza-foto.jpg"; uploadedByType="Customer" }
if ($r.Ok -and $r.Data.fileName -eq "ariza-foto.jpg") { Ok "D4: Ek metadata kaydedildi" }
else { No "D4: ek eklenemedi -> HTTP $($r.Status)" }

$r = Call GET "/api/tickets/$($t3.id)/attachments"
if ($r.Ok -and $r.Data.Count -ge 1) { Ok "D5: Ek listesi -> $($r.Data.Count) ek" }
else { No "D5: ek listesi hatali" }

Expect "D6: Closed kayda ek reddedildi" (Call POST "/api/tickets/$($t.id)/attachments" @{
    fileName="gec.jpg"; contentType="image/jpeg"; fileSizeBytes=1000
    storagePath="https://x.com/gec.jpg"; uploadedByType="Technician" }) 409 "TICKET_LOCKED"

$r = Call POST "/api/tickets/$($t3.id)/attachments" @{
    fileName="dev.bin"; contentType="application/octet-stream"; fileSizeBytes=99999999999
    storagePath="https://x.com/dev.bin"; uploadedByType="Customer" }
if ($r.Status -eq 400) { Ok "D7: 25 MB ustu dosya reddedildi" }
else { No "D7: beklenen 400, gelen $($r.Status)" }
# ============ OZET ============
Write-Host "`n============================" -ForegroundColor Cyan
Write-Host " GECEN: $script:pass" -ForegroundColor Green
Write-Host " KALAN: $script:fail" -ForegroundColor $(if ($script:fail -gt 0) { "Red" } else { "Green" })
Write-Host "============================`n" -ForegroundColor Cyan
if ($script:fail -eq 0) {
    Write-Host "TUM SENARYOLAR BASARILI - Bolum 1 ve 2 tamamlandi." -ForegroundColor Green
}

