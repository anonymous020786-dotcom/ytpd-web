"use client";

import { Download, History, LogOut, Moon, Sun } from "lucide-react";
import { useTheme } from "next-themes";
import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { Button, buttonVariants } from "@/components/ui/button";
import { useAuth } from "@/lib/auth-context";
import { cn } from "@/lib/utils";

export function Navbar() {
  const { username, logout } = useAuth();
  const { theme, setTheme } = useTheme();
  const pathname = usePathname();
  const router = useRouter();

  return (
    <header className="border-b bg-background/95 backdrop-blur sticky top-0 z-10">
      <div className="mx-auto flex h-14 max-w-5xl items-center justify-between px-4">
        <Link href="/" className="flex items-center gap-2 font-semibold tracking-tight">
          <Download className="h-5 w-5 text-primary" />
          Playlist Grabber
        </Link>

        <nav className="flex items-center gap-1">
          <Link
            href="/"
            className={cn(buttonVariants({ variant: pathname === "/" ? "secondary" : "ghost", size: "sm" }))}
          >
            New download
          </Link>
          <Link
            href="/jobs"
            className={cn(
              buttonVariants({ variant: pathname?.startsWith("/jobs") ? "secondary" : "ghost", size: "sm" })
            )}
          >
            <History className="h-4 w-4" />
            History
          </Link>

          <Button
            variant="ghost"
            size="icon"
            onClick={() => setTheme(theme === "dark" ? "light" : "dark")}
            aria-label="Toggle theme"
          >
            <Sun className="h-4 w-4 scale-100 dark:scale-0 transition-transform" />
            <Moon className="absolute h-4 w-4 scale-0 dark:scale-100 transition-transform" />
          </Button>

          <span className="mx-1 hidden text-sm text-muted-foreground sm:inline">{username}</span>
          <Button
            variant="ghost"
            size="icon"
            aria-label="Log out"
            onClick={() => {
              logout();
              router.push("/login");
            }}
          >
            <LogOut className="h-4 w-4" />
          </Button>
        </nav>
      </div>
    </header>
  );
}
