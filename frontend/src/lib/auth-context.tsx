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
    let cancelled = false;

    async function init() {
      const storedToken = localStorage.getItem("ytpd_token");
      const storedUsername = localStorage.getItem("ytpd_username");

      // Inside the Electron shell there's only one local user and the
      // backend only accepts loopback connections, so skip the login screen
      // entirely via the desktop-only local-token endpoint.
      if (!storedToken && typeof window !== "undefined" && window.electronAPI?.isElectron) {
        try {
          const res = await api.localLogin();
          if (cancelled) return;
          localStorage.setItem("ytpd_token", res.token);
          localStorage.setItem("ytpd_username", res.username);
          setToken(res.token);
          setUsername(res.username);
          setIsLoading(false);
          return;
        } catch {
          // Backend not ready yet or not a local-mode build - fall through
          // to the normal login screen.
        }
      }

      if (!cancelled) {
        setToken(storedToken);
        setUsername(storedUsername);
        setIsLoading(false);
      }
    }

    init();
    return () => {
      cancelled = true;
    };
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
