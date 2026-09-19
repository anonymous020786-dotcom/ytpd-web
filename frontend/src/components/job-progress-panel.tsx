"use client";

import { Archive, Download } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Progress } from "@/components/ui/progress";
import { api, ApiError } from "@/lib/api";
import { useJobProgress } from "@/lib/use-job-progress";
import { StatusBadge } from "./status-badge";

export function JobProgressPanel({ jobId }: { jobId: string }) {
  const job = useJobProgress(jobId);

  if (!job) {
    return (
      <Card>
        <CardContent className="py-10 text-center text-muted-foreground">Loading job…</CardContent>
      </Card>
    );
  }

  const completedCount = job.items.filter((i) => i.status === "Completed").length;
  const anyCompleted = completedCount > 0;

  async function handleDownload(itemId: string, title: string, ext: string) {
    try {
      await api.downloadItemFile(jobId, itemId, `${title}.${ext}`);
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Download failed");
    }
  }

  async function handleZip() {
    try {
      toast.promise(api.downloadJobZip(jobId, `playlist-${jobId.slice(0, 8)}.zip`), {
        loading: "Packaging zip…",
        success: "Zip ready",
        error: "Could not build zip",
      });
    } catch {
      // toast.promise already reports the error
    }
  }

  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between">
        <CardTitle className="text-base">
          {completedCount}/{job.items.length} complete · {job.format.toUpperCase()} · {job.quality}
        </CardTitle>
        {job.items.length > 1 && anyCompleted && (
          <Button size="sm" variant="outline" onClick={handleZip}>
            <Archive className="h-4 w-4" />
            Download all as .zip
          </Button>
        )}
      </CardHeader>
      <CardContent className="space-y-3">
        {job.items.map((item) => (
          <div key={item.id} className="flex items-center gap-3 rounded-lg border p-3">
            <div className="min-w-0 flex-1">
              <p className="truncate text-sm font-medium">{item.title}</p>
              <p className="truncate text-xs text-muted-foreground">{item.author}</p>
              {item.status !== "Completed" && item.status !== "Failed" && (
                <Progress value={Math.round(item.progress * 100)} className="mt-2 h-1.5" />
              )}
              {item.status === "Failed" && item.errorMessage && (
                <p className="mt-1 text-xs text-destructive">{item.errorMessage}</p>
              )}
            </div>
            <StatusBadge status={item.status} />
            {item.status === "Completed" && (
              <Button
                size="icon"
                variant="ghost"
                aria-label="Download file"
                onClick={() => handleDownload(item.id, item.title, job.format.toLowerCase())}
              >
                <Download className="h-4 w-4" />
              </Button>
            )}
          </div>
        ))}
      </CardContent>
    </Card>
  );
}
