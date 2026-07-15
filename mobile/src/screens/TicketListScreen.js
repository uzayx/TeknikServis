import { Picker } from '@react-native-picker/picker';
import { useCallback, useEffect, useState } from 'react';
import {
  ActivityIndicator, FlatList, Modal, RefreshControl, ScrollView,
  StyleSheet, Text, TextInput, TouchableOpacity, View,
} from 'react-native';
import { api } from '../api';
import {
  ALL_PRIORITIES, ALL_STATUSES, DATE_OPTIONS, PRIORITY_COLORS, PRIORITY_LABELS,
  SLA_OPTIONS, SORT_OPTIONS, STATUS_COLORS, STATUS_LABELS,
} from '../constants';

const PAGE_SIZE = 20;
const EMPTY_FILTERS = {
  status: null, priority: null, customerId: null,
  technicianId: null, slaViolated: null, dateKey: 'all',
};

export default function TicketListScreen({ navigation }) {
  const [items, setItems] = useState([]);
  const [meta, setMeta] = useState({ page: 1, totalPages: 0, totalCount: 0 });
  const [hasNext, setHasNext] = useState(false);
  const [loading, setLoading] = useState(false);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState(null);

  // Uygulanmis filtreler (sorguyu bunlar tetikler)
  const [filters, setFilters] = useState(EMPTY_FILTERS);
  const [sortKey, setSortKey] = useState(SORT_OPTIONS[0].key);
  const [search, setSearch] = useState('');

  // Modal icindeki gecici secimler: "Uygula"ya basilana kadar sorgu atilmaz,
  // boylece her dokunusta gereksiz istek gitmez.
  const [filterOpen, setFilterOpen] = useState(false);
  const [sortOpen, setSortOpen] = useState(false);
  const [draft, setDraft] = useState(EMPTY_FILTERS);

  // Filtre panelindeki secicileri beslemek icin
  const [customers, setCustomers] = useState([]);
  const [technicians, setTechnicians] = useState([]);

  useEffect(() => {
    api.getCustomers().then(setCustomers).catch(() => {});
    api.getTechnicians().then((list) => setTechnicians(list.filter((t) => t.isActive))).catch(() => {});
  }, []);

  const activeFilterCount = Object.entries(filters).filter(([k, v]) =>
    k === 'dateKey' ? v !== 'all' : v !== null
  ).length;
  const activeSortLabel = SORT_OPTIONS.find((o) => o.key === sortKey).label;

  const load = useCallback(async (pageToLoad, replace) => {
    setLoading(true);
    setError(null);
    const sort = SORT_OPTIONS.find((o) => o.key === sortKey);
    const dateOpt = DATE_OPTIONS.find((d) => d.key === filters.dateKey);
    const createdFrom = dateOpt.hours
      ? new Date(Date.now() - dateOpt.hours * 3600 * 1000).toISOString()
      : null;
    try {
      const result = await api.getTickets({
        page: pageToLoad,
        pageSize: PAGE_SIZE,
        status: filters.status,
        priority: filters.priority,
        customerId: filters.customerId,
        technicianId: filters.technicianId,
        slaViolated: filters.slaViolated,
        createdFrom,
        search: search.trim() || null,
        sortBy: sort.sortBy,
        sortDir: sort.sortDir,
      });
      setItems(replace ? result.items : (prev) => [...prev, ...result.items]);
      setMeta({ page: result.page, totalPages: result.totalPages, totalCount: result.totalCount });
      setHasNext(result.hasNextPage);
    } catch (e) {
      setError(e.message);
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, [filters, sortKey, search]);

  useEffect(() => { load(1, true); }, [filters, sortKey]);
  useEffect(() => {
    const unsub = navigation.addListener('focus', () => load(1, true));
    return unsub;
  }, [navigation, load]);

  const openFilters = () => { setDraft(filters); setFilterOpen(true); };
  const applyFilters = () => { setFilters(draft); setFilterOpen(false); };

  const renderItem = ({ item }) => {
    const slaOver = new Date(item.slaDeadline) < new Date()
      && !['Completed', 'Approved', 'Closed'].includes(item.status);
    return (
      <TouchableOpacity
        style={styles.card}
        onPress={() => navigation.navigate('TicketDetail', { id: item.id })}
      >
        <View style={styles.cardHeader}>
          <Text style={styles.ticketNumber}>{item.ticketNumber}</Text>
          <View style={[styles.badge, { backgroundColor: STATUS_COLORS[item.status] }]}>
            <Text style={styles.badgeText}>{STATUS_LABELS[item.status]}</Text>
          </View>
        </View>
        <Text style={styles.title} numberOfLines={1}>{item.title}</Text>
        <View style={styles.cardFooter}>
          <Text style={styles.meta}>{item.customerName}</Text>
          <Text style={[styles.meta, { color: PRIORITY_COLORS[item.priority], fontWeight: '600' }]}>
            {PRIORITY_LABELS[item.priority]}
          </Text>
        </View>
        <View style={styles.cardFooter}>
          <Text style={styles.meta}>
            {item.technicianName ? `Teknisyen: ${item.technicianName}` : 'Teknisyen atanmadi'}
          </Text>
          {slaOver && <Text style={styles.slaTag}>SLA ASILDI</Text>}
        </View>
      </TouchableOpacity>
    );
  };

  return (
    <View style={styles.container}>
      <TextInput
        style={styles.search}
        placeholder="Kayit no, baslik, musteri veya teknisyen ara..."
        value={search}
        onChangeText={setSearch}
        onSubmitEditing={() => load(1, true)}
        returnKeyType="search"
      />

      {/* Aktif filtre/siralama butonlarin uzerinde gorunur: panel kapaliyken
          kullanicinin "neden bu kadar az kayit var?" diye sormamasi icin. */}
      <View style={styles.toolbar}>
        <TouchableOpacity
          style={[styles.toolBtn, activeFilterCount > 0 && styles.toolBtnActive]}
          onPress={openFilters}
        >
          <Text style={activeFilterCount > 0 ? styles.toolTextActive : styles.toolText}>
            Filtre{activeFilterCount > 0 ? ` (${activeFilterCount})` : ''}
          </Text>
        </TouchableOpacity>

        <TouchableOpacity style={styles.toolBtn} onPress={() => setSortOpen(true)}>
          <Text style={styles.toolText}>Sirala: {activeSortLabel}</Text>
        </TouchableOpacity>

        {activeFilterCount > 0 && (
          <TouchableOpacity style={styles.clearBtn} onPress={() => setFilters(EMPTY_FILTERS)}>
            <Text style={styles.clearText}>Temizle</Text>
          </TouchableOpacity>
        )}
      </View>

      <View style={styles.pageInfo}>
        <Text style={styles.pageInfoText}>
          {meta.totalCount} kayit
          {meta.totalPages > 0 ? `  -  sayfa ${meta.page}/${meta.totalPages}` : ''}
        </Text>
      </View>

      {error && <Text style={styles.error}>{error}</Text>}

      <FlatList
        data={items}
        keyExtractor={(item) => item.id}
        renderItem={renderItem}
        contentContainerStyle={{ paddingBottom: 90 }}
        onEndReached={() => { if (hasNext && !loading) load(meta.page + 1, false); }}
        onEndReachedThreshold={0.4}
        refreshControl={
          <RefreshControl refreshing={refreshing} onRefresh={() => { setRefreshing(true); load(1, true); }} />
        }
        ListFooterComponent={loading ? <ActivityIndicator style={{ margin: 16 }} /> : null}
        ListEmptyComponent={!loading ? <Text style={styles.empty}>Kayit bulunamadi.</Text> : null}
      />

      <TouchableOpacity style={styles.fab} onPress={() => navigation.navigate('CreateTicket')}>
        <Text style={styles.fabText}>+ Yeni Kayit</Text>
      </TouchableOpacity>

      {/* --- Filtre paneli --- */}
      <Modal visible={filterOpen} animationType="slide" transparent onRequestClose={() => setFilterOpen(false)}>
        <TouchableOpacity style={styles.sheetBg} activeOpacity={1} onPress={() => setFilterOpen(false)}>
          <TouchableOpacity style={styles.sheet} activeOpacity={1}>
            <View style={styles.sheetHandle} />
            <Text style={styles.sheetTitle}>Filtreler</Text>

            <ScrollView>
              <Text style={styles.groupLabel}>Durum</Text>
              <View style={styles.chipWrap}>
                {[null, ...ALL_STATUSES].map((s) => (
                  <TouchableOpacity
                    key={s ?? 'all'}
                    style={[styles.chip, draft.status === s && styles.chipActive]}
                    onPress={() => setDraft({ ...draft, status: s })}
                  >
                    <Text style={draft.status === s ? styles.chipTextActive : styles.chipText}>
                      {s ? STATUS_LABELS[s] : 'Tumu'}
                    </Text>
                  </TouchableOpacity>
                ))}
              </View>

              <Text style={styles.groupLabel}>Oncelik</Text>
              <View style={styles.chipWrap}>
                {[null, ...ALL_PRIORITIES].map((p) => (
                  <TouchableOpacity
                    key={p ?? 'allp'}
                    style={[
                      styles.chip,
                      draft.priority === p && styles.chipActive,
                      p && draft.priority !== p && { borderColor: PRIORITY_COLORS[p] },
                    ]}
                    onPress={() => setDraft({ ...draft, priority: p })}
                  >
                    <Text style={draft.priority === p ? styles.chipTextActive : styles.chipText}>
                      {p ? PRIORITY_LABELS[p] : 'Tumu'}
                    </Text>
                  </TouchableOpacity>
                ))}
              </View>

              <Text style={styles.groupLabel}>SLA Durumu</Text>
              <View style={styles.chipWrap}>
                {SLA_OPTIONS.map((o) => (
                  <TouchableOpacity
                    key={String(o.value)}
                    style={[
                      styles.chip,
                      draft.slaViolated === o.value && styles.chipActive,
                      o.value === true && draft.slaViolated !== true && { borderColor: '#dc2626' },
                    ]}
                    onPress={() => setDraft({ ...draft, slaViolated: o.value })}
                  >
                    <Text style={draft.slaViolated === o.value ? styles.chipTextActive : styles.chipText}>
                      {o.label}
                    </Text>
                  </TouchableOpacity>
                ))}
              </View>

              <Text style={styles.groupLabel}>Olusturulma</Text>
              <View style={styles.chipWrap}>
                {DATE_OPTIONS.map((d) => (
                  <TouchableOpacity
                    key={d.key}
                    style={[styles.chip, draft.dateKey === d.key && styles.chipActive]}
                    onPress={() => setDraft({ ...draft, dateKey: d.key })}
                  >
                    <Text style={draft.dateKey === d.key ? styles.chipTextActive : styles.chipText}>
                      {d.label}
                    </Text>
                  </TouchableOpacity>
                ))}
              </View>

              <Text style={styles.groupLabel}>Musteri</Text>
              <View style={styles.pickerWrap}>
                <Picker
                  selectedValue={draft.customerId}
                  onValueChange={(v) => setDraft({ ...draft, customerId: v })}
                >
                  <Picker.Item label="Tum musteriler" value={null} />
                  {customers.map((c) => (
                    <Picker.Item key={c.id} label={`${c.fullName} (${c.phone})`} value={c.id} />
                  ))}
                </Picker>
              </View>

              <Text style={styles.groupLabel}>Teknisyen</Text>
              <View style={styles.pickerWrap}>
                <Picker
                  selectedValue={draft.technicianId}
                  onValueChange={(v) => setDraft({ ...draft, technicianId: v })}
                >
                  <Picker.Item label="Tum teknisyenler" value={null} />
                  {technicians.map((t) => (
                    <Picker.Item
                      key={t.id}
                      label={`${t.fullName}${t.specialty ? ' - ' + t.specialty : ''}`}
                      value={t.id}
                    />
                  ))}
                </Picker>
              </View>
            </ScrollView>

            <View style={styles.sheetActions}>
              <TouchableOpacity style={styles.sheetCancel} onPress={() => setDraft(EMPTY_FILTERS)}>
                <Text style={styles.sheetCancelText}>Temizle</Text>
              </TouchableOpacity>
              <TouchableOpacity style={styles.sheetApply} onPress={applyFilters}>
                <Text style={styles.sheetApplyText}>Uygula</Text>
              </TouchableOpacity>
            </View>
          </TouchableOpacity>
        </TouchableOpacity>
      </Modal>

      {/* --- Siralama paneli --- */}
      <Modal visible={sortOpen} animationType="slide" transparent onRequestClose={() => setSortOpen(false)}>
        <TouchableOpacity style={styles.sheetBg} activeOpacity={1} onPress={() => setSortOpen(false)}>
          <TouchableOpacity style={styles.sheet} activeOpacity={1}>
            <View style={styles.sheetHandle} />
            <Text style={styles.sheetTitle}>Siralama</Text>
            {SORT_OPTIONS.map((o) => (
              <TouchableOpacity
                key={o.key}
                style={[styles.sortRow, sortKey === o.key && styles.sortRowActive]}
                onPress={() => { setSortKey(o.key); setSortOpen(false); }}
              >
                <Text style={sortKey === o.key ? styles.sortTextActive : styles.sortText}>{o.label}</Text>
                {sortKey === o.key && <Text style={styles.check}>secili</Text>}
              </TouchableOpacity>
            ))}
          </TouchableOpacity>
        </TouchableOpacity>
      </Modal>
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: '#f3f4f6' },
  search: {
    backgroundColor: '#fff', marginHorizontal: 12, marginTop: 12,
    paddingHorizontal: 14, paddingVertical: 10, borderRadius: 8,
    borderWidth: 1, borderColor: '#e5e7eb', fontSize: 13,
  },
  toolbar: { flexDirection: 'row', paddingHorizontal: 12, paddingTop: 10, gap: 8 },
  toolBtn: {
    paddingHorizontal: 14, paddingVertical: 8, borderRadius: 8,
    backgroundColor: '#fff', borderWidth: 1, borderColor: '#e5e7eb',
  },
  toolBtnActive: { backgroundColor: '#111827', borderColor: '#111827' },
  toolText: { color: '#374151', fontSize: 13, fontWeight: '500' },
  toolTextActive: { color: '#fff', fontSize: 13, fontWeight: '600' },
  clearBtn: { paddingHorizontal: 10, paddingVertical: 8, justifyContent: 'center' },
  clearText: { color: '#dc2626', fontSize: 12, fontWeight: '600' },
  pageInfo: { paddingHorizontal: 14, paddingTop: 8, paddingBottom: 2 },
  pageInfoText: { fontSize: 11, color: '#9ca3af' },
  card: {
    backgroundColor: '#fff', marginHorizontal: 12, marginVertical: 5,
    padding: 14, borderRadius: 10, borderWidth: 1, borderColor: '#e5e7eb',
  },
  cardHeader: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' },
  ticketNumber: { fontSize: 12, color: '#6b7280', fontWeight: '600' },
  badge: { paddingHorizontal: 8, paddingVertical: 3, borderRadius: 10 },
  badgeText: { color: '#fff', fontSize: 11, fontWeight: '600' },
  title: { fontSize: 15, fontWeight: '600', marginTop: 6, color: '#111827' },
  cardFooter: { flexDirection: 'row', justifyContent: 'space-between', marginTop: 4 },
  meta: { fontSize: 12, color: '#6b7280' },
  slaTag: {
    fontSize: 10, color: '#fff', backgroundColor: '#dc2626',
    paddingHorizontal: 6, paddingVertical: 2, borderRadius: 6, fontWeight: '700',
  },
  empty: { textAlign: 'center', marginTop: 40, color: '#6b7280' },
  error: { color: '#dc2626', marginHorizontal: 12, marginBottom: 4 },
  fab: {
    position: 'absolute', bottom: 32, right: 16,
    backgroundColor: '#111827', paddingHorizontal: 18, paddingVertical: 12,
    borderRadius: 24, elevation: 4,
  },
  fabText: { color: '#fff', fontWeight: '600' },
  sheetBg: { flex: 1, backgroundColor: 'rgba(0,0,0,0.4)', justifyContent: 'flex-end' },
  sheet: {
    backgroundColor: '#fff', borderTopLeftRadius: 16, borderTopRightRadius: 16,
    padding: 16, paddingBottom: 24, maxHeight: '85%',
  },
  sheetHandle: {
    width: 40, height: 4, backgroundColor: '#d1d5db', borderRadius: 2,
    alignSelf: 'center', marginBottom: 12,
  },
  sheetTitle: { fontSize: 17, fontWeight: '700', color: '#111827', marginBottom: 8 },
  groupLabel: { fontSize: 13, fontWeight: '600', color: '#6b7280', marginTop: 14, marginBottom: 8 },
  chipWrap: { flexDirection: 'row', flexWrap: 'wrap', gap: 8 },
  chip: {
    paddingHorizontal: 14, paddingVertical: 8, borderRadius: 16,
    backgroundColor: '#fff', borderWidth: 1, borderColor: '#e5e7eb',
  },
  chipActive: { backgroundColor: '#111827', borderColor: '#111827' },
  chipText: { color: '#374151', fontSize: 13 },
  chipTextActive: { color: '#fff', fontSize: 13 },
  pickerWrap: {
    backgroundColor: '#fff', borderRadius: 8, borderWidth: 1, borderColor: '#e5e7eb',
  },
  sheetActions: { flexDirection: 'row', marginTop: 18, gap: 10 },
  sheetCancel: {
    flex: 1, padding: 14, borderRadius: 10, alignItems: 'center',
    backgroundColor: '#fff', borderWidth: 1, borderColor: '#e5e7eb',
  },
  sheetCancelText: { color: '#374151', fontWeight: '600', fontSize: 15 },
  sheetApply: { flex: 2, backgroundColor: '#111827', padding: 14, borderRadius: 10, alignItems: 'center' },
  sheetApplyText: { color: '#fff', fontWeight: '600', fontSize: 15 },
  sortRow: {
    flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center',
    paddingVertical: 14, paddingHorizontal: 12, borderRadius: 8, marginTop: 6,
    borderWidth: 1, borderColor: '#e5e7eb',
  },
  sortRowActive: { backgroundColor: '#111827', borderColor: '#111827' },
  sortText: { color: '#374151', fontSize: 15 },
  sortTextActive: { color: '#fff', fontSize: 15, fontWeight: '600' },
  check: { color: '#9ca3af', fontSize: 12 },
});
