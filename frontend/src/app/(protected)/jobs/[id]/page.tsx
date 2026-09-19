"use client";

import { use } from "react";
import { JobProgressPanel } from "@/components/job-progress-panel";

export default function JobDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  return <JobProgressPanel jobId={id} />;
}
