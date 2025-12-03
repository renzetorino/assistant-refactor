// src/AuthContext.jsx
import React, { createContext, useContext, useEffect, useState } from "react";
import { supabase } from "./supabase";

const AuthContext = createContext();

export const AuthProvider = ({ children }) => {
  const [session, setSession] = useState(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    // 1) read persisted session on startup
    const load = async () => {
      const { data } = await supabase.auth.getSession();
      setSession(data?.session ?? null);
      setLoading(false);
    };
    load();

    // 2) listen for session changes (login/logout)
    const { data: { subscription } = {} } = supabase.auth.onAuthStateChange((_event, session) => {
      setSession(session);
    });

    return () => subscription?.unsubscribe?.();
  }, []);

  // optional helper to sign out from anywhere:
  const signOut = async () => {
    await supabase.auth.signOut();
    // don't clear everything from localStorage here — remove only your keys if any
    localStorage.removeItem("userProfile");
    setSession(null);
  };

  return (
    <AuthContext.Provider value={{ session, loading, signOut }}>
      {children}
    </AuthContext.Provider>
  );
};

export const useAuth = () => useContext(AuthContext);
