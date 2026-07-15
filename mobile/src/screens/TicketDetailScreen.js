import { useCallback, useEffect, useState } from 'react';
import {
  ActivityIndicator, ScrollView, StyleSheet,
  Text, TextInput, TouchableOpacity, View,
} from 'react-native';
import { api } from '../api';
import { PRIORITY_LABELS, STATUS_COLORS, STATUS_LABELS } from '../constants';

function formatDate(iso) {
  if (!iso) return '-';
  const d = new Date(iso);
  return d.toLocaleDateString('tr-TR') + ' ' + d.toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' });
}

function Row({ label, value }) {
  return (
    <View style={styles.row}>
      <Text style={styles.rowLabel}>{label}</Text>
      <Text style={styles.rowValue}>{value}</Text>
    </View>
  );
}

export default function TicketDetailScreen({ route, navigation }) {
  const { id } = route.params;
  const [ticket, setTicket] = useState(null);
  const [comments, setComments] = useState([]);
  const [error, setError] = useState(null);
  const [newComment, setNewComment] = useState('');
  const [commentBusy, setCommentBusy] = useState(false);
  const [commentError, setCommentError] = useState(null);

  const load = useCallback(async () => {
    try {
      const [t, c] = await Promise.all([api.getTicket(id), api.getComments(id)]);
      setTicket(t);
      setComments(c);
      setError(null);
    } catch (e) {
      setError(e.message);
    }
  }, [id]);

  useEffect(() => {
    const unsub = navigation.addListener('focus', load);
    return unsub;
  }, [navigation, load]);

  const sendComment = async () => {
    setCommentBusy(true);
    setCommentError(null);
    try {
      await api.addComment(id, { authorType: 'Technician', content: newComment.trim() });
      setNewComment('');
      setComments(await api.getComments(id));
    } catch (e) {
      setCommentError(e.message);
    } finally {
      setCommentBusy(false);
    }
  };

  if (error) return <Text style={styles.error}>{error}</Text>;
  if (!ticket) return <ActivityIndicator style={{ marginTop: 40 }} />;

  const slaOver = new Date(ticket.slaDeadline) < new Date() && !ticket.completedAt;
  const canUpdate = ticket.allowedNextStatuses.length > 0 || ticket.status === 'New';
  const canComment = ticket.status !== 'Closed';

  return (
    <ScrollView style={styles.container} contentContainerStyle={{ paddingBottom: 32 }}>
      <View style={styles.card}>
        <View style={styles.headerRow}>
          <Text style={styles.ticketNumber}>{ticket.ticketNumber}</Text>
          <View style={[styles.badge, { backgroundColor: STATUS_COLORS[ticket.status] }]}>
            <Text style={styles.badgeText}>{STATUS_LABELS[ticket.status]}</Text>
          </View>
        </View>
        <Text style={styles.title}>{ticket.title}</Text>
        <Text style={styles.description}>{ticket.description}</Text>
      </View>

      <View style={styles.card}>
        <Text style={styles.sectionTitle}>Bilgiler</Text>
        <Row label="Musteri" value={ticket.customerName} />
        <Row label="Teknisyen" value={ticket.technicianName ?? 'Atanmadi'} />
        <Row label="Oncelik" value={PRIORITY_LABELS[ticket.priority]} />
        <Row label="Olusturulma" value={formatDate(ticket.createdAt)} />
        <Row label="SLA Bitis" value={formatDate(ticket.slaDeadline)} />
        {slaOver && <Text style={styles.slaWarn}>SLA suresi asildi!</Text>}
        {ticket.completedAt && <Row label="Tamamlanma" value={formatDate(ticket.completedAt)} />}
        {ticket.closedAt && <Row label="Kapanis" value={formatDate(ticket.closedAt)} />}
      </View>

      {canUpdate && (
        <TouchableOpacity
          style={styles.updateBtn}
          onPress={() => navigation.navigate('UpdateStatus', { ticket })}
        >
          <Text style={styles.updateBtnText}>Durumu Guncelle</Text>
        </TouchableOpacity>
      )}

      <View style={styles.card}>
        <Text style={styles.sectionTitle}>Durum Gecmisi ({ticket.statusHistories.length})</Text>
        {ticket.statusHistories.map((h) => (
          <View key={h.id} style={styles.historyItem}>
            <Text style={styles.historyText}>
              {h.fromStatus ? `${STATUS_LABELS[h.fromStatus]} -> ` : ''}{STATUS_LABELS[h.toStatus]}
              <Text style={styles.historyBy}>  ({h.changedByType})</Text>
            </Text>
            {h.note ? <Text style={styles.historyNote}>{h.note}</Text> : null}
            <Text style={styles.historyDate}>{formatDate(h.changedAt)}</Text>
          </View>
        ))}
      </View>

      <View style={styles.card}>
        <Text style={styles.sectionTitle}>Yorumlar ({comments.length})</Text>
        {comments.length === 0 && <Text style={styles.meta}>Henuz yorum yok.</Text>}
        {comments.map((c) => (
          <View key={c.id} style={styles.historyItem}>
            <Text style={styles.historyBy}>{c.authorType}</Text>
            <Text style={styles.historyText}>{c.content}</Text>
            <Text style={styles.historyDate}>{formatDate(c.createdAt)}</Text>
          </View>
        ))}

        {canComment ? (
          <View style={styles.commentBox}>
            <TextInput
              style={styles.commentInput}
              placeholder="Yorum ekle..."
              value={newComment}
              onChangeText={setNewComment}
              multiline
              maxLength={2000}
            />
            <TouchableOpacity
              style={[styles.commentBtn, (!newComment.trim() || commentBusy) && { opacity: 0.5 }]}
              onPress={sendComment}
              disabled={!newComment.trim() || commentBusy}
            >
              <Text style={styles.commentBtnText}>{commentBusy ? '...' : 'Gonder'}</Text>
            </TouchableOpacity>
          </View>
        ) : (
          <Text style={styles.meta}>Kapatilmis kayda yorum eklenemez.</Text>
        )}
        {commentError && <Text style={styles.error}>{commentError}</Text>}
      </View>
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: '#f3f4f6' },
  card: {
    backgroundColor: '#fff', margin: 12, marginBottom: 0,
    padding: 14, borderRadius: 10, borderWidth: 1, borderColor: '#e5e7eb',
  },
  headerRow: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' },
  ticketNumber: { fontSize: 13, color: '#6b7280', fontWeight: '600' },
  badge: { paddingHorizontal: 10, paddingVertical: 4, borderRadius: 12 },
  badgeText: { color: '#fff', fontSize: 12, fontWeight: '600' },
  title: { fontSize: 18, fontWeight: '700', marginTop: 8, color: '#111827' },
  description: { fontSize: 14, color: '#374151', marginTop: 6, lineHeight: 20 },
  sectionTitle: { fontSize: 15, fontWeight: '700', marginBottom: 8, color: '#111827' },
  row: { flexDirection: 'row', justifyContent: 'space-between', paddingVertical: 4 },
  rowLabel: { color: '#6b7280', fontSize: 13 },
  rowValue: { color: '#111827', fontSize: 13, fontWeight: '500', flexShrink: 1, textAlign: 'right' },
  slaWarn: { color: '#dc2626', fontWeight: '700', marginTop: 6 },
  updateBtn: {
    backgroundColor: '#111827', margin: 12, marginBottom: 0,
    padding: 14, borderRadius: 10, alignItems: 'center',
  },
  updateBtnText: { color: '#fff', fontWeight: '600', fontSize: 15 },
  historyItem: { borderTopWidth: 1, borderTopColor: '#f3f4f6', paddingVertical: 8 },
  historyText: { fontSize: 13, color: '#111827' },
  historyBy: { fontSize: 12, color: '#6b7280' },
  historyNote: { fontSize: 12, color: '#374151', marginTop: 2, fontStyle: 'italic' },
  historyDate: { fontSize: 11, color: '#9ca3af', marginTop: 2 },
  meta: { fontSize: 13, color: '#6b7280' },
  commentBox: { flexDirection: 'row', marginTop: 10, gap: 8, alignItems: 'flex-end' },
  commentInput: {
    flex: 1, backgroundColor: '#f9fafb', borderRadius: 8, borderWidth: 1,
    borderColor: '#e5e7eb', paddingHorizontal: 10, paddingVertical: 8,
    fontSize: 13, maxHeight: 80,
  },
  commentBtn: { backgroundColor: '#111827', paddingHorizontal: 16, paddingVertical: 10, borderRadius: 8 },
  commentBtnText: { color: '#fff', fontWeight: '600', fontSize: 13 },
  error: { color: '#dc2626', marginTop: 8 },
});
