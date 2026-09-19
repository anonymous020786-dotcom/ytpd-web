export {};

declare global {
  interface Window {
    // Present only when this page is running inside the Electron desktop
    // shell (exposed by desktop/src/preload.ts via contextBridge).
    electronAPI?: {
      isElectron: true;
      platform: NodeJS.Platform;
      getDownloadFolder: () => Promise<string>;
      chooseDownloadFolder: () => Promise<string | null>;
      openPath: (path: string) => Promise<void>;
    };
  }
}
