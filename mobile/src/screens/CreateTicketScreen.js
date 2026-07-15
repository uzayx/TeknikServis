import { Picker } from '@react-native-picker/picker';
import { useEffect, useState } from 'react';
import {
  ActivityIndicator, Modal, ScrollView, StyleSheet,
  Text, TextInput, TouchableOpacity, View,
} from 'react-native';
import { api } from '../api';
import { PRIORITY_LABELS } from '../constants';

export default function CreateTicketScreen({ navigation }) {
  const [customers, setCustomers] = useState(null);
  const [customerId, setCustomerId] = useState(null);
  const [customerSearch, setCustomerSearch] = useState('');
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [priority, setPriority] = useState('Medium');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState(null);

  // Yeni musteri modali
  const [modalOpen, setModalOpen] = useState(false);
  const [nc, setNc] = useState({ firstName: '', lastName: '', email: '', phone: '', address: '' });
  const [modalError, setModalError] = useState(null);
  const [modalSaving, setModalSaving] = useState(false);

  const loadCustomers = () =>
    api.getCustomers()
      .then((list) => {
        setCustomers(list);
        return list;
      })
      .catch((e) => { setError(e.message); return []; });

  useEffect(() => {
    loadCustomers().then((list) => {
      if (list.length > 0) setCustomerId(list[0].id);
    });
  }, []);

  // 200+ musteride dropdown hantallasiyor: arama ile listeyi daraltiyoruz.
  const filtered = (customers ?? []).filter((c) => {
    const q = customerSearch.trim().toLowerCase();
    if (!q) return true;
    return c.fullName.toLowerCase().includes(q) || c.phone.includes(q);
  });

  const saveCustomer = async () => {
    setModalSaving(true);
    setModalError(null);
    try {
      const created = await api.createCustomer({
        firstName: nc.firstName,
        lastName: nc.lastName,
        email: nc.email,
        phone: nc.phone,
        address: nc.address || null,
      });
      const list = await loadCustomers();
      setCustomerId(created.id);
      setCustomerSearch('');
      setModalOpen(false);
      setNc({ firstName: '', lastName: '', email: '', phone: '', address: '' });
    } catch (e) {
      setModalError(e.message);
    } finally {
      setModalSaving(false);
    }
  };

  const submit = async () => {
    setSaving(true);
    setError(null);
    try {
      const ticket = await api.createTicket({ customerId, title, description, priority });
      navigation.replace('TicketDetail', { id: ticket.id });
    } catch (e) {
      setError(e.message);
    } finally {
      setSaving(false);
    }
  };

  if (!customers) return error
    ? <Text style={styles.error}>{error}</Text>
    : <ActivityIndicator style={{ marginTop: 40 }} />;

  return (
    <ScrollView style={styles.container} contentContainerStyle={{ padding: 12 }}>
      <View style={styles.labelRow}>
        <Text style={styles.label}>Musteri</Text>
        <TouchableOpacity onPress={() => setModalOpen(true)}>
          <Text style={styles.link}>+ Yeni Musteri</Text>
        </TouchableOpacity>
      </View>

      <TextInput
        style={styles.input}
        placeholder="Musteri ara (isim veya telefon)..."
        value={customerSearch}
        onChangeText={setCustomerSearch}
      />
      <View style={[styles.pickerWrap, { marginTop: 6 }]}>
        <Picker selectedValue={customerId} onValueChange={setCustomerId}>
          {filtered.length === 0
            ? <Picker.Item label="Sonuc yok" value={null} />
            : filtered.map((c) => (
                <Picker.Item key={c.id} label={`${c.fullName}  (${c.phone})`} value={c.id} />
              ))}
        </Picker>
      </View>
      <Text style={styles.hint}>{filtered.length} / {customers.length} musteri</Text>

      <Text style={styles.label}>Baslik</Text>
      <TextInput
        style={styles.input}
        placeholder="Orn: Kombi isitmiyor"
        value={title}
        onChangeText={setTitle}
        maxLength={200}
      />

      <Text style={styles.label}>Aciklama</Text>
      <TextInput
        style={[styles.input, styles.multiline]}
        placeholder="Arizanin detayi..."
        value={description}
        onChangeText={setDescription}
        multiline
        maxLength={2000}
      />

      <Text style={styles.label}>Oncelik</Text>
      <View style={styles.pickerWrap}>
        <Picker selectedValue={priority} onValueChange={setPriority}>
          {Object.entries(PRIORITY_LABELS).map(([value, label]) => (
            <Picker.Item key={value} label={label} value={value} />
          ))}
        </Picker>
      </View>

      {error && <Text style={styles.error}>{error}</Text>}

      <TouchableOpacity
        style={[styles.submitBtn, saving && { opacity: 0.6 }]}
        onPress={submit}
        disabled={saving}
      >
        <Text style={styles.submitText}>{saving ? 'Kaydediliyor...' : 'Kaydi Olustur'}</Text>
      </TouchableOpacity>

      <Modal visible={modalOpen} animationType="slide" transparent>
        <View style={styles.modalBg}>
          <View style={styles.modalBox}>
            <Text style={styles.modalTitle}>Yeni Musteri</Text>
            <ScrollView>
              <TextInput style={styles.input} placeholder="Ad" value={nc.firstName}
                onChangeText={(v) => setNc({ ...nc, firstName: v })} maxLength={100} />
              <TextInput style={[styles.input, { marginTop: 8 }]} placeholder="Soyad" value={nc.lastName}
                onChangeText={(v) => setNc({ ...nc, lastName: v })} maxLength={100} />
              <TextInput style={[styles.input, { marginTop: 8 }]} placeholder="E-posta" value={nc.email}
                onChangeText={(v) => setNc({ ...nc, email: v })} autoCapitalize="none"
                keyboardType="email-address" maxLength={150} />
              <TextInput style={[styles.input, { marginTop: 8 }]} placeholder="Telefon" value={nc.phone}
                onChangeText={(v) => setNc({ ...nc, phone: v })} keyboardType="phone-pad" maxLength={20} />
              <TextInput style={[styles.input, { marginTop: 8 }]} placeholder="Adres (istege bagli)"
                value={nc.address} onChangeText={(v) => setNc({ ...nc, address: v })} maxLength={500} />
              {modalError && <Text style={styles.error}>{modalError}</Text>}
            </ScrollView>
            <View style={styles.modalActions}>
              <TouchableOpacity style={styles.modalCancel} onPress={() => { setModalOpen(false); setModalError(null); }}>
                <Text style={styles.modalCancelText}>Vazgec</Text>
              </TouchableOpacity>
              <TouchableOpacity
                style={[styles.modalSave, modalSaving && { opacity: 0.6 }]}
                onPress={saveCustomer}
                disabled={modalSaving}
              >
                <Text style={styles.submitText}>{modalSaving ? 'Kaydediliyor...' : 'Kaydet'}</Text>
              </TouchableOpacity>
            </View>
          </View>
        </View>
      </Modal>
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: '#f3f4f6' },
  labelRow: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', marginTop: 12 },
  label: { fontSize: 13, fontWeight: '600', color: '#374151', marginTop: 12, marginBottom: 4 },
  link: { fontSize: 13, fontWeight: '600', color: '#2563eb', marginTop: 12 },
  hint: { fontSize: 11, color: '#9ca3af', marginTop: 4 },
  input: {
    backgroundColor: '#fff', borderRadius: 8, borderWidth: 1, borderColor: '#e5e7eb',
    paddingHorizontal: 12, paddingVertical: 10, fontSize: 14,
  },
  multiline: { height: 100, textAlignVertical: 'top' },
  pickerWrap: { backgroundColor: '#fff', borderRadius: 8, borderWidth: 1, borderColor: '#e5e7eb' },
  submitBtn: { backgroundColor: '#111827', padding: 14, borderRadius: 10, alignItems: 'center', marginTop: 20 },
  submitText: { color: '#fff', fontWeight: '600', fontSize: 15 },
  error: { color: '#dc2626', marginTop: 10 },
  modalBg: { flex: 1, backgroundColor: 'rgba(0,0,0,0.5)', justifyContent: 'center', padding: 20 },
  modalBox: { backgroundColor: '#f3f4f6', borderRadius: 12, padding: 16, maxHeight: '80%' },
  modalTitle: { fontSize: 17, fontWeight: '700', marginBottom: 12, color: '#111827' },
  modalActions: { flexDirection: 'row', marginTop: 14, gap: 10 },
  modalCancel: {
    flex: 1, padding: 14, borderRadius: 10, alignItems: 'center',
    backgroundColor: '#fff', borderWidth: 1, borderColor: '#e5e7eb',
  },
  modalCancelText: { color: '#374151', fontWeight: '600', fontSize: 15 },
  modalSave: { flex: 1, backgroundColor: '#111827', padding: 14, borderRadius: 10, alignItems: 'center' },
});
