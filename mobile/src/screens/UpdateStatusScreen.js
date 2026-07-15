import { Picker } from '@react-native-picker/picker';
import { useEffect, useState } from 'react';
import {
  ActivityIndicator, ScrollView, StyleSheet,
  Text, TextInput, TouchableOpacity, View,
} from 'react-native';
import { api } from '../api';
import { STATUS_COLORS, STATUS_LABELS } from '../constants';

// Onemli tasarim karari: bu ekran state machine'i BILMIYOR.
// Hangi gecislerin gecerli oldugunu API soyluyor (allowedNextStatuses);
// ekran yalnizca o listeyi butona ceviriyor. Is kurali degisirse
// mobil uygulamayi guncellemeye gerek kalmiyor.
export default function UpdateStatusScreen({ route, navigation }) {
  const { ticket } = route.params;
  const [technicians, setTechnicians] = useState(null);
  const [technicianId, setTechnicianId] = useState(null);
  const [note, setNote] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);

  const needsAssignment = ticket.status === 'New';

  useEffect(() => {
    if (!needsAssignment) return;
    api.getTechnicians()
      .then((list) => {
        const active = list.filter((t) => t.isActive);
        setTechnicians(active);
        if (active.length > 0) setTechnicianId(active[0].id);
      })
      .catch((e) => setError(e.message));
  }, [needsAssignment]);

  const assign = async () => {
    setBusy(true);
    setError(null);
    try {
      await api.assignTechnician(ticket.id, {
        technicianId,
        changedByType: 'Center',
        note: note.trim() || null,
      });
      navigation.goBack();
    } catch (e) {
      setError(e.message);
      setBusy(false);
    }
  };

  const changeStatus = async (newStatus) => {
    setBusy(true);
    setError(null);
    try {
      await api.changeStatus(ticket.id, {
        newStatus,
        changedByType: 'Center',
        note: note.trim() || null,
      });
      navigation.goBack();
    } catch (e) {
      setError(e.message);
      setBusy(false);
    }
  };

  return (
    <ScrollView style={styles.container} contentContainerStyle={{ padding: 12 }}>
      <View style={styles.card}>
        <Text style={styles.ticketNumber}>{ticket.ticketNumber}</Text>
        <Text style={styles.title}>{ticket.title}</Text>
        <Text style={styles.current}>
          Mevcut durum: {STATUS_LABELS[ticket.status]}
        </Text>
      </View>

      <Text style={styles.label}>Not (istege bagli)</Text>
      <TextInput
        style={styles.input}
        placeholder="Islemle ilgili kisa not..."
        value={note}
        onChangeText={setNote}
        maxLength={500}
      />

      {needsAssignment && (
        <>
          <Text style={styles.label}>Teknisyen Ata</Text>
          {!technicians ? (
            <ActivityIndicator />
          ) : (
            <>
              <View style={styles.pickerWrap}>
                <Picker selectedValue={technicianId} onValueChange={setTechnicianId}>
                  {technicians.map((t) => (
                    <Picker.Item
                      key={t.id}
                      label={`${t.fullName}${t.specialty ? ' - ' + t.specialty : ''}`}
                      value={t.id}
                    />
                  ))}
                </Picker>
              </View>
              <TouchableOpacity
                style={[styles.actionBtn, { backgroundColor: '#7c3aed' }, busy && { opacity: 0.6 }]}
                onPress={assign}
                disabled={busy}
              >
                <Text style={styles.actionText}>Teknisyeni Ata (durum: Atandi olur)</Text>
              </TouchableOpacity>
            </>
          )}
        </>
      )}

      {ticket.allowedNextStatuses.length > 0 && !needsAssignment && (
        <>
          <Text style={styles.label}>Sonraki Durum</Text>
          {ticket.allowedNextStatuses.map((s) => (
            <TouchableOpacity
              key={s}
              style={[styles.actionBtn, { backgroundColor: STATUS_COLORS[s] }, busy && { opacity: 0.6 }]}
              onPress={() => changeStatus(s)}
              disabled={busy}
            >
              <Text style={styles.actionText}>{STATUS_LABELS[s]} durumuna gecir</Text>
            </TouchableOpacity>
          ))}
        </>
      )}

      {error && <Text style={styles.error}>{error}</Text>}
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: '#f3f4f6' },
  card: {
    backgroundColor: '#fff', padding: 14, borderRadius: 10,
    borderWidth: 1, borderColor: '#e5e7eb',
  },
  ticketNumber: { fontSize: 12, color: '#6b7280', fontWeight: '600' },
  title: { fontSize: 16, fontWeight: '700', marginTop: 4, color: '#111827' },
  current: { fontSize: 13, color: '#374151', marginTop: 6 },
  label: { fontSize: 13, fontWeight: '600', color: '#374151', marginTop: 16, marginBottom: 4 },
  input: {
    backgroundColor: '#fff', borderRadius: 8, borderWidth: 1, borderColor: '#e5e7eb',
    paddingHorizontal: 12, paddingVertical: 10, fontSize: 14,
  },
  pickerWrap: {
    backgroundColor: '#fff', borderRadius: 8, borderWidth: 1, borderColor: '#e5e7eb',
    marginBottom: 8,
  },
  actionBtn: { padding: 14, borderRadius: 10, alignItems: 'center', marginTop: 8 },
  actionText: { color: '#fff', fontWeight: '600', fontSize: 14 },
  error: { color: '#dc2626', marginTop: 12 },
});
