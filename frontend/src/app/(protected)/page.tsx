"use client";

import { Loader2, Search, Sparkles } from "lucide-react";
import { useState } from "react";
import { toast } from "sonner";
import { JobProgressPanel } from "@/components/job-progress-panel";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { VideoPickerCard } from "@/components/video-picker-card";
import { api, ApiError } from "@/lib/api";
import type { DownloadFormat, ResolveResponse } from "@/lib/types";

const QUALITIES = ["best", "2160p", "1440p", "1080p", "720p", "480p", "360p"];

export default function DashboardPage() {
  const [url, setUrl] = useState("");
  const [isResolving, setIsResolving] = useState(false);
  const [result, setResult] = useState<ResolveResponse | null>(null);
  const [selected, setSelected] = useState<Set<string>>(new Set());

  const [format, setFormat] = useState<DownloadFormat>("Mp4");
  const [quality, setQuality] = useState("best");
  const [embedMetadata, setEmbedMetadata] = useState(true);
  const [isCreating, setIsCreating] = useState(false);
  const [activeJobId, setActiveJobId] = useState<string | null>(null);

  const isVideoFormat = format === "Mp4" || format === "Mkv";

  async function handleResolve() {
    if (!url.trim()) return;
    setIsResolving(true);
    setResult(null);
    setActiveJobId(null);
    try {
      const res = await api.resolve(url.trim());
      setResult(res);
      setSelected(new Set(res.items.map((i) => i.videoId)));
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Could not resolve that URL.");
    } finally {
      setIsResolving(false);
    }
  }

  function toggleOne(videoId: string, checked: boolean) {
    setSelected((prev) => {
      const next = new Set(prev);
      if (checked) next.add(videoId);
      else next.delete(videoId);
      return next;
    });
  }

  function toggleAll() {
    if (!result) return;
    setSelected((prev) =>
      prev.size === result.items.length ? new Set() : new Set(result.items.map((i) => i.videoId))
    );
  }

  async function handleStartDownload() {
    if (!result) return;
    const items = result.items
      .filter((i) => selected.has(i.videoId))
      .map((i) => ({ videoId: i.videoId, title: i.title, author: i.author }));

    if (items.length === 0) {
      toast.error("Select at least one video.");
      return;
    }

    setIsCreating(true);
    try {
      const job = await api.createJob({
        sourceUrl: url.trim(),
        items,
        format,
        quality,
        embedMetadata,
      });
      setActiveJobId(job.id);
      toast.success(`Queued ${items.length} download${items.length > 1 ? "s" : ""}.`);
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Could not start the download.");
    } finally {
      setIsCreating(false);
    }
  }

  return (
    <div className="space-y-6">
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-lg">
            <Sparkles className="h-5 w-5 text-primary" />
            Paste a YouTube link
          </CardTitle>
        </CardHeader>
        <CardContent>
          <div className="flex gap-2">
            <Input
              placeholder="Video, playlist, or channel URL…"
              value={url}
              onChange={(e) => setUrl(e.target.value)}
              onKeyDown={(e) => e.key === "Enter" && handleResolve()}
            />
            <Button onClick={handleResolve} disabled={isResolving || !url.trim()}>
              {isResolving ? <Loader2 className="h-4 w-4 animate-spin" /> : <Search className="h-4 w-4" />}
              Resolve
            </Button>
          </div>
        </CardContent>
      </Card>

      {result && (
        <Card>
          <CardHeader className="flex flex-row items-start justify-between gap-4">
            <div>
              <CardTitle className="text-lg">{result.title}</CardTitle>
              <p className="text-sm text-muted-foreground">
                {result.kind} · {result.items.length} video{result.items.length !== 1 && "s"}
                {result.truncated && " (showing first 300)"}
              </p>
            </div>
            {result.items.length > 1 && (
              <Button variant="outline" size="sm" onClick={toggleAll}>
                {selected.size === result.items.length ? "Deselect all" : "Select all"}
              </Button>
            )}
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="max-h-96 space-y-2 overflow-y-auto pr-1">
              {result.items.map((video) => (
                <VideoPickerCard
                  key={video.videoId}
                  video={video}
                  checked={selected.has(video.videoId)}
                  onToggle={(checked) => toggleOne(video.videoId, checked)}
                />
              ))}
            </div>

            <div className="flex flex-wrap items-end gap-4 rounded-lg border bg-muted/30 p-4">
              <div className="space-y-1.5">
                <Label>Format</Label>
                <Select value={format} onValueChange={(v) => setFormat(v as DownloadFormat)}>
                  <SelectTrigger className="w-32">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="Mp4">MP4 (video)</SelectItem>
                    <SelectItem value="Mkv">MKV (video)</SelectItem>
                    <SelectItem value="Mp3">MP3 (audio)</SelectItem>
                    <SelectItem value="M4a">M4A (audio)</SelectItem>
                    <SelectItem value="Wav">WAV (audio)</SelectItem>
                  </SelectContent>
                </Select>
              </div>

              {isVideoFormat && (
                <div className="space-y-1.5">
                  <Label>Quality</Label>
                  <Select value={quality} onValueChange={(v) => setQuality(v ?? "best")}>
                    <SelectTrigger className="w-28">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {QUALITIES.map((q) => (
                        <SelectItem key={q} value={q}>
                          {q === "best" ? "Best" : q}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>
              )}

              {!isVideoFormat && (
                <div className="flex items-center gap-2 pb-2">
                  <Checkbox
                    id="tag"
                    checked={embedMetadata}
                    onCheckedChange={(v) => setEmbedMetadata(v === true)}
                  />
                  <Label htmlFor="tag" className="font-normal">
                    Auto-tag title &amp; artist
                  </Label>
                </div>
              )}

              <Button className="ml-auto" onClick={handleStartDownload} disabled={isCreating || selected.size === 0}>
                {isCreating && <Loader2 className="h-4 w-4 animate-spin" />}
                Download {selected.size > 0 && `(${selected.size})`}
              </Button>
            </div>
          </CardContent>
        </Card>
      )}

      {activeJobId && <JobProgressPanel jobId={activeJobId} />}
    </div>
  );
}
