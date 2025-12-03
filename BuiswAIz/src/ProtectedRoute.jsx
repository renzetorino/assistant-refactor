// src/ProtectedRoute.jsx
import React from "react";
import { Navigate } from "react-router-dom";
import { useAuth } from "./AuthContext";

const ProtectedRoute = ({ children }) => {
  const { session, loading } = useAuth();
  if (loading) return <div>Loading...</div>;
  return session ? children : <Navigate to="/login" replace />;
};

export default ProtectedRoute;
