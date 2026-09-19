"use client";

import * as signalR from "@microsoft/signalr";
import { useEffect, useRef, useState } from "react";
import { api, getStoredToken } from "./api";
import type { JobItemStatusDto, JobStatusDto } from "./types";

// Loads a job's current state, then keeps it live via the SignalR progress
// hub so per-video status/progress updates without polling.
export function useJobProgress(jobId: string | null) {
  const [job, setJob] = useState<JobStatusDto | null>(null);
  const connectionRef = useRef<signalR.HubConnection | null>(null);

  useEffect(() => {
    if (!jobId) return;
    let cancelled = false;

    api.getJob(jobId).then((data) => {
      if (!cancelled) setJob(data);
    });

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`${api.baseUrl}/hubs/progress`, {
        accessTokenFactory: () => getStoredToken() ?? "",
      })
      .withAutomaticReconnect()
      .build();

    connection.on("itemUpdated", (update: JobItemStatusDto) => {
      setJob((prev) => {
        if (!prev || prev.id !== jobId) return prev;
        return {
          ...prev,
          items: prev.items.map((i) => (i.id === update.id ? update : i)),
        };
      });
    });

    connectionRef.current = connection;
    connection
      .start()
      .then(() => {
        if (cancelled) return;
        return connection.invoke("JoinJob", jobId);
      })
      .catch((err) => {
        // React's Strict Mode double-invokes effects in dev, which stops
        // this connection mid-negotiation on the throwaway first mount.
        // That's expected there, not a real failure.
        if (!cancelled) console.error("SignalR connection failed", err);
      });

    return () => {
      cancelled = true;
      connection.stop().catch(() => {});
    };
  }, [jobId]);

  return job;
}
