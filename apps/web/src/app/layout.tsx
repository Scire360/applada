import type { Metadata } from "next";
import type { ReactNode } from "react";
import { MatchDemoProvider } from "@/components/match-demo-provider";
import "./globals.css";

export const metadata: Metadata = {
  title: "Applada — futebol que aproxima",
  description: "Organize partidas, reúna seu time e viva o futebol amador.",
};

export default function RootLayout({ children }: Readonly<{ children: ReactNode }>) {
  return (
    <html data-scroll-behavior="smooth" lang="pt-BR">
      <body>
        <MatchDemoProvider>{children}</MatchDemoProvider>
      </body>
    </html>
  );
}
