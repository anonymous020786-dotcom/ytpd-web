export type ResolvedVideo = {
  videoId: string;
  title: string;
  author: string;
  thumbnailUrl: string;
  durationSeconds: number | null;
};

export type ResolveResponse = {
  kind: "Video" | "Playlist" | "Channel";
  title: string;
  author: string | null;
  thumbnailUrl: string | null;
  items: ResolvedVideo[];
  truncated: boolean;
};

export type DownloadFormat = "Mp4" | "Mkv" | "Webm" | "Mp3" | "M4a" | "Wav" | "Opus";

export const VIDEO_FORMATS: DownloadFormat[] = ["Mp4", "Mkv", "Webm"];

export type JobItemStatus =
  | "Queued"
  | "Resolving"
  | "Downloading"
  | "Converting"
  | "Tagging"
  | "Completed"
  | "Failed"
  | "Cancelled";

export type JobItemStatusDto = {
  id: string;
  videoId: string;
  title: string;
  author: string;
  status: JobItemStatus;
  progress: number;
  errorMessage: string | null;
  outputFileName: string | null;
};

export type JobStatusDto = {
  id: string;
  createdAt: string;
  sourceUrl: string;
  format: DownloadFormat;
  quality: string;
  items: JobItemStatusDto[];
};
