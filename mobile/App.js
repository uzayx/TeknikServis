import { NavigationContainer } from '@react-navigation/native';
import { createNativeStackNavigator } from '@react-navigation/native-stack';
import CreateTicketScreen from './src/screens/CreateTicketScreen';
import TicketDetailScreen from './src/screens/TicketDetailScreen';
import TicketListScreen from './src/screens/TicketListScreen';
import UpdateStatusScreen from './src/screens/UpdateStatusScreen';

const Stack = createNativeStackNavigator();

export default function App() {
  return (
    <NavigationContainer>
      <Stack.Navigator
        screenOptions={{
          headerStyle: { backgroundColor: '#111827' },
          headerTintColor: '#fff',
        }}
      >
        <Stack.Screen name="TicketList" component={TicketListScreen} options={{ title: 'Ariza Kayitlari' }} />
        <Stack.Screen name="TicketDetail" component={TicketDetailScreen} options={{ title: 'Kayit Detayi' }} />
        <Stack.Screen name="CreateTicket" component={CreateTicketScreen} options={{ title: 'Yeni Ariza Kaydi' }} />
        <Stack.Screen name="UpdateStatus" component={UpdateStatusScreen} options={{ title: 'Durum Guncelle' }} />
      </Stack.Navigator>
    </NavigationContainer>
  );
}
