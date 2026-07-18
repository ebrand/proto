import { Navigate, Route, Routes } from 'react-router-dom';
import { RequireAuth } from './components/RequireAuth';
import { Login } from './pages/Login';
import { Authenticate } from './pages/Authenticate';
import { Dashboard } from './pages/Dashboard';
import { Account } from './pages/Account';
import { Prototypes } from './pages/Prototypes';
import { PrototypeDetail } from './pages/PrototypeDetail';

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<Login />} />
      <Route path="/authenticate" element={<Authenticate />} />
      <Route
        path="/dashboard"
        element={
          <RequireAuth>
            <Dashboard />
          </RequireAuth>
        }
      />
      <Route
        path="/account"
        element={
          <RequireAuth>
            <Account />
          </RequireAuth>
        }
      />
      <Route
        path="/prototypes"
        element={
          <RequireAuth>
            <Prototypes />
          </RequireAuth>
        }
      />
      <Route
        path="/prototypes/:id"
        element={
          <RequireAuth>
            <PrototypeDetail />
          </RequireAuth>
        }
      />
      <Route path="/" element={<Navigate to="/dashboard" replace />} />
      <Route path="*" element={<Navigate to="/dashboard" replace />} />
    </Routes>
  );
}
