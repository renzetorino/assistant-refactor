// src/App.jsx
import React from "react";
import { BrowserRouter, Routes, Route, Navigate } from "react-router-dom";
import { ToastContainer } from "react-toastify";
import "react-toastify/dist/ReactToastify.css";

import { AuthProvider } from "./AuthContext";
import ProtectedRoute from "./ProtectedRoute";

import Login from "./login";
import Inventory from "./inventory/inventory";
import Supplier from "./supplier/supplier";
import ForgotPassword from './forgotpassword';
import ResetPassword from './ResetPassword';
import Assistant from './Assistant/Assistant';
import Sales from "./TablePage";
import Dashboard from "./Dashboard";
import ExpenseDashboard from "./expenses/expenses";
import UploadSheets from "./components/UploadSheets";
import BudgetHistory from "./budget/BudgetHistory";
import PointOfSales from "./PointOfSales";
import OnlineOrders from "./onlineOrders/OnlineOrders";
import PlannedPaymentsPage from "./pages/PlannedPaymentsPage";
import Signup from "./Signup";
import SetupBusiness from './SetupBusiness';
import CreateBusiness from './createBusiness';
import JoinBusiness from './joinBusiness';
// COMMENTED OUT: Business Maturity feature removed (did not meet team/advisor standards)
// import MaturityReport from './pages/MaturityReport';

function App() {
  return (
    <div className="App">
      <ToastContainer position="top-right" autoClose={3000} />

      <BrowserRouter>
        <AuthProvider>
          <Routes>
            <Route path="/" element={<Navigate to="/login" replace />} />

            {/* Public routes */}
            <Route path="/login" element={<Login />} />
            <Route path="/signup" element={<Signup />} />
            <Route path="/forgot-password" element={<ForgotPassword />} />
            <Route path="/reset-password" element={<ResetPassword />} />
            <Route path="/setup-business" element={<SetupBusiness />} />
            <Route path="/create-business" element={<CreateBusiness/>} />
             <Route path="/join-business" element={<JoinBusiness />} />

            {/* Protected routes */}
            <Route path="/Dashboard" element={<ProtectedRoute><Dashboard /></ProtectedRoute>} />
            <Route path="/inventory" element={<ProtectedRoute><Inventory /></ProtectedRoute>} />
            <Route path="/supplier" element={<ProtectedRoute><Supplier /></ProtectedRoute>} />
            <Route path="/TablePage" element={<ProtectedRoute><Sales /></ProtectedRoute>} />
            <Route path="/pos" element={<ProtectedRoute><PointOfSales /></ProtectedRoute>} />
            <Route path="/online-orders" element={<ProtectedRoute><OnlineOrders /></ProtectedRoute>} />
            <Route path="/assistant" element={<ProtectedRoute><Assistant /></ProtectedRoute>} />
            <Route path="/expenses" element={<ProtectedRoute><ExpenseDashboard /></ProtectedRoute>} />
            <Route path="/upload" element={<ProtectedRoute><UploadSheets /></ProtectedRoute>} />
            <Route path="/PlannedPaymentsPage" element={<ProtectedRoute><PlannedPaymentsPage /></ProtectedRoute>} />
            <Route path="/budget-history" element={<ProtectedRoute><BudgetHistory /></ProtectedRoute>} />
            {/* COMMENTED OUT: Business Maturity route removed (did not meet team/advisor standards) */}
            {/* <Route path="/maturity-report" element={<ProtectedRoute><MaturityReport /></ProtectedRoute>} /> */}

            <Route path="*" element={<Navigate to="/login" replace />} />
          </Routes>
        </AuthProvider>
      </BrowserRouter>
    </div>
  );
}

export default App;
