"use client";

import { createContext, useContext, useEffect, useState, type ReactNode } from "react";
import { api } from "./api";

type AuthState = {
  token: string | null;
  username: string | null;
  isLoading: boolean;
  login: (username: string, password: string) => Promise<void>;
  logout: () => void;
};

const AuthContext = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [token, setToken] = useState<string | null>(null);
  const [username, setUsername] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    // One-time sync from localStorage (an external system) on mount; this is
    // the documented exception to "don't setState in effects".
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setToken(localStorage.getItem("ytpd_token"));
    setUsername(localStorage.getItem("ytpd_username"));
    setIsLoading(false);
  }, []);

  async function login(username: string, password: string) {
    const res = await api.login(username, password);
    localStorage.setItem("ytpd_token", res.token);
    localStorage.setItem("ytpd_username", res.username);
    setToken(res.token);
    setUsername(res.username);
  }

  function logout() {
    localStorage.removeItem("ytpd_token");
    localStorage.removeItem("ytpd_username");
    setToken(null);
    setUsername(null);
  }

  return (
    <AuthContext.Provider value={{ token, username, isLoading, login, logout }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used within AuthProvider");
  return ctx;
}
