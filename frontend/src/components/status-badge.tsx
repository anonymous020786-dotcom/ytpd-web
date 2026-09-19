import { Ban, CheckCircle2, Download, Loader2, Tag, XCircle } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import type { JobItemStatus } from "@/lib/types";

const STATUS_META: Record<JobItemStatus, { label: string; variant: "default" | "secondary" | "destructive" | "outline"; icon: React.ReactNode }> = {
  Queued: { label: "Queued", variant: "outline", icon: null },
  Resolving: { label: "Resolving", variant: "secondary", icon: <Loader2 className="h-3 w-3 animate-spin" /> },
  Downloading: { label: "Downloading", variant: "secondary", icon: <Download className="h-3 w-3" /> },
  Converting: { label: "Converting", variant: "secondary", icon: <Loader2 className="h-3 w-3 animate-spin" /> },
  Tagging: { label: "Tagging", variant: "secondary", icon: <Tag className="h-3 w-3" /> },
  Completed: { label: "Done", variant: "default", icon: <CheckCircle2 className="h-3 w-3" /> },
  Failed: { label: "Failed", variant: "destructive", icon: <XCircle className="h-3 w-3" /> },
  Cancelled: { label: "Cancelled", variant: "outline", icon: <Ban className="h-3 w-3" /> },
};

export function StatusBadge({ status }: { status: JobItemStatus }) {
  const meta = STATUS_META[status];
  return (
    <Badge variant={meta.variant} className="gap-1">
      {meta.icon}
      {meta.label}
    </Badge>
  );
}
