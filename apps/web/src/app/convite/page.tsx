import type { Metadata } from "next";
import { AcceptInvitation } from "@/components/groups/accept-invitation";

export const metadata: Metadata = { title: "Convite para um grupo · Applada", referrer: "no-referrer" };

// Página pública: não usa o AppShell, que exige sessão. O token vem no hash e nunca chega ao servidor.
export default function InvitationPage() {
  return (
    <main className="invite-page connected-page">
      <AcceptInvitation />
    </main>
  );
}
