"use client";

import { Checkbox } from "@/components/ui/checkbox";
import { formatDuration } from "@/lib/format";
import type { ResolvedVideo } from "@/lib/types";

export function VideoPickerCard({
  video,
  checked,
  onToggle,
}: {
  video: ResolvedVideo;
  checked: boolean;
  onToggle: (checked: boolean) => void;
}) {
  return (
    <label className="flex cursor-pointer items-center gap-3 rounded-lg border p-2 transition-colors hover:bg-accent/50">
      <Checkbox checked={checked} onCheckedChange={(v) => onToggle(v === true)} />
      {/* eslint-disable-next-line @next/next/no-img-element */}
      <img
        src={video.thumbnailUrl}
        alt=""
        className="h-12 w-20 shrink-0 rounded object-cover bg-muted"
        loading="lazy"
      />
      <div className="min-w-0 flex-1">
        <p className="truncate text-sm font-medium leading-snug">{video.title}</p>
        <p className="truncate text-xs text-muted-foreground">
          {video.author}
          {video.durationSeconds !== null && ` · ${formatDuration(video.durationSeconds)}`}
        </p>
      </div>
    </label>
  );
}
