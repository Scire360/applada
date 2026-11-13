"use client";

import Link from "next/link";
import { useMemo, useState } from "react";
import { Icon } from "@/components/icon";

export type DirectoryRecord = {
  initials: string;
  name: string;
  detail: string;
  state: string;
  color: string;
};

type ScreenDirectoryProps = {
  records: DirectoryRecord[];
  related: { href: string; label: string }[];
};

export function ScreenDirectory({ records, related }: ScreenDirectoryProps) {
  const [query, setQuery] = useState("");
  const visibleRecords = useMemo(() => {
    const normalizedQuery = query.trim().toLocaleLowerCase("pt-BR");
    if (!normalizedQuery) return records;
    return records.filter((record) =>
      `${record.name} ${record.detail} ${record.state}`.toLocaleLowerCase("pt-BR").includes(normalizedQuery),
    );
  }, [query, records]);

  return (
    <>
      <label className="directory-search">
        <Icon name="search" />
        <span className="sr-only">Filtrar a lista desta prévia</span>
        <input onChange={(event) => setQuery(event.target.value)} placeholder="Filtrar esta lista..." value={query} />
      </label>
      <div aria-live="polite" className="directory-list">
        {visibleRecords.length ? visibleRecords.map((record) => (
          <article className="directory-row" key={`${record.name}-${record.detail}`}>
            <span className={`avatar avatar-soft avatar-${record.color}`}>{record.initials}</span>
            <span className="directory-copy">
              <strong>{record.name}</strong>
              <span>{record.detail}</span>
            </span>
            <span className="directory-state"><span className="status-dot" />{record.state}</span>
          </article>
        )) : (
          <p className="directory-empty">Nenhum item desta prévia corresponde à busca.</p>
        )}
      </div>
      <div className="related-links">
        {related.map((link) => (
          <Link className="related-link" href={link.href} key={link.href}>
            {link.label}<Icon name="arrow-right" />
          </Link>
        ))}
      </div>
    </>
  );
}
