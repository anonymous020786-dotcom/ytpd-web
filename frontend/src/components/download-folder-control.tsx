"use client";

import { Folder, FolderOpen } from "lucide-react";
import { useEffect, useState } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { useIsElectron } from "@/lib/use-is-electron";

// Desktop-only (Electron): choosing/opening a local download folder has no
// meaning in a browser tab.
export function DownloadFolderControl() {
  const isElectron = useIsElectron();
  const [folder, setFolder] = useState<string | null>(null);

  useEffect(() => {
    if (!isElectron) return;
    window.electronAPI?.getDownloadFolder().then(setFolder);
  }, [isElectron]);

  if (!isElectron) return null;

  async function handleChoose() {
    const chosen = await window.electronAPI?.chooseDownloadFolder();
    if (chosen) {
      setFolder(chosen);
      toast.success("Downloads folder updated");
    }
  }

  return (
    <div className="flex items-center gap-1">
      <Button
        variant="ghost"
        size="sm"
        title={folder ?? undefined}
        onClick={() => folder && window.electronAPI?.openPath(folder)}
      >
        <FolderOpen className="h-4 w-4" />
        <span className="hidden max-w-40 truncate lg:inline">{folder}</span>
      </Button>
      <Button variant="ghost" size="icon" aria-label="Change downloads folder" onClick={handleChoose}>
        <Folder className="h-4 w-4" />
      </Button>
    </div>
  );
}
