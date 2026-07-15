-- ============================================================
-- 04-indexes.sql — Onerilen index'ler
-- Gerekceler ve EXPLAIN ANALYZE olcumleri: sql/03-indexes.md
--
-- Kullanim: 01-seed.sql calistirildiktan sonra uygulanir.
-- Uretimde CREATE INDEX CONCURRENTLY tercih edilmelidir (tablo kilitlenmesin);
-- bu script tek kullanicili gelistirme ortami icin yazilmistir.
--
-- NOT: Ucuncu bir aday (ix_tickets_active — partial index) degerlendirildi ve
-- OLCUM SONUCU REDDEDILDI: tek basina calisiyor (cost 11.30) ancak asagidaki
-- covering index varken planlayici ona donup bakmiyor (cost 3.85). Detay ve
-- planlar icin 03-indexes.md.
-- ============================================================

-- ---------- Oneri 1: Covering index — SLA taramasi ve aktif kayit listeleri ----------
-- Hedef: Sorgu 2A / 2B (SLA ihlalleri) + operasyon ekrani "en acil aktif kayitlar".
-- Olculen kazanc (SLA sorgusu): Seq Scan+Sort -> Index Only Scan (Heap Fetches: 0),
--   cost 304.95 -> 5.67, buffers 140 -> 3, exec 0.973ms -> 0.047ms.
-- Maliyet: 280 kB (en buyuk index) ve her INSERT/UPDATE'te guncellenir.
DROP INDEX IF EXISTS ix_tickets_sla_covering;
CREATE INDEX ix_tickets_sla_covering
ON service_tickets ("SlaDeadline")
INCLUDE ("TicketNumber", "Status", "Priority", "CompletedAt");

-- ---------- Oneri 2: Covering partial index — teknisyen performansi ----------
-- Hedef: Sorgu 4.
-- INCLUDE("AssignedAt") kritik: bu kolon olmadan planlayici index'i tamamen yok sayiyor,
-- cunku sorgu CompletedAt - AssignedAt hesapliyor ve her satir icin heap'e gitmek zorunda.
-- Olculen kazanc: Seq Scan+HashAggregate -> Index Only Scan+GroupAggregate,
--   cost 201.21 -> 86.87, buffers 140 -> 13.
DROP INDEX IF EXISTS ix_tickets_tech_completed;
CREATE INDEX ix_tickets_tech_completed
ON service_tickets ("AssignedTechnicianId", "CompletedAt")
INCLUDE ("AssignedAt")
WHERE "CompletedAt" IS NOT NULL AND "AssignedAt" IS NOT NULL;

-- Reddedilen aday temizligi (onceki calistirmalardan kalmis olabilir)
DROP INDEX IF EXISTS ix_tickets_active;

-- Index Only Scan icin gorunurluk haritasinin guncel olmasi sart:
VACUUM ANALYZE service_tickets;

-- ---------- Dogrulama ----------
SELECT indexname, pg_size_pretty(pg_relation_size(quote_ident(indexname)::regclass)) AS boyut
FROM pg_indexes
WHERE tablename = 'service_tickets'
ORDER BY pg_relation_size(quote_ident(indexname)::regclass) DESC;
