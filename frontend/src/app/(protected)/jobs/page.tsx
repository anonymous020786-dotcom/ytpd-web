"use client";

import { Trash2 } from "lucide-react";
import Link from "next/link";
import { useEffect, useState } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { api, ApiError } from "@/lib/api";
import type { JobStatusDto } from "@/lib/types";

export default function JobsHistoryPage() {
  const [jobs, setJobs] = useState<JobStatusDto[] | null>(null);

  useEffect(() => {
    api.listJobs().then(setJobs).catch(() => setJobs([]));
  }, []);

  async function handleDelete(id: string) {
    try {
      await api.deleteJob(id);
      setJobs((prev) => prev?.filter((j) => j.id !== id) ?? null);
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Could not delete job.");
    }
  }

  if (!jobs) {
    return <p className="text-muted-foreground">Loading…</p>;
  }

  if (jobs.length === 0) {
    return <p className="text-muted-foreground">No downloads yet.</p>;
  }

  return (
    <div className="space-y-3">
      {jobs.map((job) => {
        const completed = job.items.filter((i) => i.status === "Completed").length;
        return (
          <Card key={job.id}>
            <CardContent className="flex items-center justify-between gap-4 py-4">
              <Link href={`/jobs/${job.id}`} className="min-w-0 flex-1">
                <p className="truncate text-sm font-medium">{job.sourceUrl}</p>
                <p className="text-xs text-muted-foreground">
                  {new Date(job.createdAt).toLocaleString()} · {job.format.toUpperCase()} ·{" "}
                  {completed}/{job.items.length} complete
                </p>
              </Link>
              <Button variant="ghost" size="icon" aria-label="Delete job" onClick={() => handleDelete(job.id)}>
                <Trash2 className="h-4 w-4" />
              </Button>
            </CardContent>
          </Card>
        );
      })}
    </div>
  );
}
