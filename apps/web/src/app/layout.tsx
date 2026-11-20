import type { Metadata } from "next";
import type { ReactNode } from "react";
import { MatchDemoProvider } from "@/components/match-demo-provider";
import { AccountProvider } from "@/components/account-provider";
import { AccountDialog } from "@/components/account-dialog";
import { getApiMode } from "@/lib/api-mode";
import "@fontsource-variable/oswald";
import "./globals.css";

export const metadata: Metadata = {
  title: "Applada — futebol que aproxima",
  description: "Organize partidas, reúna seu time e viva o futebol amador.",
};

export default function RootLayout({ children }: Readonly<{ children: ReactNode }>) {
  const apiMode = getApiMode();
  return (
    <html data-scroll-behavior="smooth" lang="pt-BR">
      <body>
        <AccountProvider apiMode={apiMode}>
          <MatchDemoProvider>{children}</MatchDemoProvider>
          <AccountDialog />
        </AccountProvider>
      </body>
    </html>
  );
}
