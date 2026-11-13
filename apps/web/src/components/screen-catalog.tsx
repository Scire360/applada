"use client";

import Link from "next/link";
import { useMemo, useState } from "react";
import { Icon } from "@/components/icon";
import type { ScreenGroup } from "@/lib/screens";

export type ScreenCatalogItem = {
  href: string;
  label: string;
  group: ScreenGroup;
};

type ScreenCatalogProps = {
  items: ScreenCatalogItem[];
};

const groupLabels: Record<ScreenGroup, string> = {
  matches: "Partidas",
  players: "Elenco",
  community: "Comunidade",
  finance: "Financeiro",
  analytics: "Estatísticas",
  admin: "Administração e ferramentas",
};

const groupOrder: ScreenGroup[] = [
  "matches",
  "players",
  "community",
  "finance",
  "analytics",
  "admin",
];

export function ScreenCatalog({ items }: ScreenCatalogProps) {
  const [query, setQuery] = useState("");
  const visibleItems = useMemo(() => {
    const normalizedQuery = query.trim().toLocaleLowerCase("pt-BR");
    if (!normalizedQuery) return items;
    return items.filter((item) =>
      `${item.label} ${item.href} ${groupLabels[item.group]}`
        .toLocaleLowerCase("pt-BR")
        .includes(normalizedQuery),
    );
  }, [items, query]);
  const visibleGroups = groupOrder
    .map((group) => ({
      group,
      items: visibleItems.filter((item) => item.group === group),
    }))
    .filter((section) => section.items.length > 0);

  return (
    <>
      <label className="directory-search">
        <Icon name="search" />
        <span className="sr-only">Buscar uma área do Applada</span>
        <input
          onChange={(event) => setQuery(event.target.value)}
          placeholder="Buscar por nome ou caminho..."
          value={query}
        />
      </label>
      <p aria-live="polite" className="screen-catalog-count" role="status">
        {visibleItems.length} {visibleItems.length === 1 ? "área encontrada" : "áreas encontradas"}
      </p>
      {visibleGroups.length ? (
        <div className="screen-catalog-groups">
          {visibleGroups.map(({ group, items: groupItems }) => (
            <section aria-label={groupLabels[group]} className="screen-catalog-group" key={group}>
              <h3>{groupLabels[group]}</h3>
              <div className="screen-catalog-grid">
                {groupItems.map((item) => (
                  <Link className="screen-catalog-link" href={item.href} key={item.href}>
                    <span>{item.label}</span>
                    <small>{item.href}</small>
                    <Icon name="arrow-right" />
                  </Link>
                ))}
              </div>
            </section>
          ))}
        </div>
      ) : (
        <p className="directory-empty">Nenhuma área corresponde à busca.</p>
      )}
    </>
  );
}
