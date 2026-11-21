"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { loginHref } from "@/lib/callback-url";

// Saída para a sessão expirada: volta para a tela atual depois do login.
export function SignInAgain() {
  const pathname = usePathname();
  return (
    <Link className="button button-primary" href={loginHref(pathname, typeof window === "undefined" ? "" : window.location.search)}>
      Entrar novamente
    </Link>
  );
}
