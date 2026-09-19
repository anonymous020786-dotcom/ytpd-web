"use client";

import { useEffect, useState } from "react";

// Client-only check (see download-folder-control.tsx for why this can't be
// read during the initial render).
export function useIsElectron() {
  const [isElectron, setIsElectron] = useState(false);

  useEffect(() => {
    // One-time sync from an external, client-only global on mount - the
    // documented exception to "don't setState in effects".
    if (window.electronAPI?.isElectron) {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setIsElectron(true);
    }
  }, []);

  return isElectron;
}
