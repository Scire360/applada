"use client";

import Link from "next/link";
import { useEffect, useRef, useState, type FormEvent } from "react";
import { useMatchDemo } from "@/components/match-demo-provider";
import type { MatchGoal } from "@/components/match-demo-provider";

type GameDemoPanelProps = {
  mode: "live" | "highlights";
};

function MatchRequired({ children }: { children: React.ReactNode }) {
  return (
    <div className="match-demo-empty">
      <span className="eyebrow">PRÓXIMO PASSO</span>
      <p>{children}</p>
      <Link className="button button-primary" href="/eventos/novo">Organizar uma pelada</Link>
    </div>
  );
}

export function GameDemoPanel({ mode }: GameDemoPanelProps) {
  const {
    match,
    status,
    checkedIn,
    goals,
    playerOfTheMatch,
    startMatch,
    finishMatch,
    recordGoal,
    removeGoal,
    voteForPlayer,
  } = useMatchDemo();
  const [notice, setNotice] = useState("");
  const [confirmEnding, setConfirmEnding] = useState(false);
  const endMatchButtonRef = useRef<HTMLButtonElement>(null);
  const continueButtonRef = useRef<HTMLButtonElement>(null);
  const voteHighlightLinkRef = useRef<HTMLAnchorElement>(null);
  const wasConfirmingEnding = useRef(false);

  useEffect(() => {
    if (confirmEnding) {
      continueButtonRef.current?.focus();
    } else if (wasConfirmingEnding.current) {
      if (status === "completed") voteHighlightLinkRef.current?.focus();
      else endMatchButtonRef.current?.focus();
    }
    wasConfirmingEnding.current = confirmEnding;
  }, [confirmEnding, status]);

  if (!match) {
    return (
      <MatchRequired>
        {mode === "live"
          ? "Organize uma pelada para liberar o placar e o registro de lances."
          : "Organize uma pelada e registre o jogo para liberar a votação de destaque."}
      </MatchRequired>
    );
  }

  if (!checkedIn.length) {
    return (
      <div className="match-demo-empty">
        <span className="eyebrow">CHECK-IN NECESSÁRIO</span>
        <p>Registre o check-in de pelo menos uma pessoa para continuar esta etapa.</p>
        <Link className="button button-primary" href="/presenca/check-in">Ir para check-in</Link>
      </div>
    );
  }

  if (mode === "highlights") {
    if (status !== "completed") {
      return (
        <div className="match-demo-empty">
          <span className="eyebrow">APÓS O APITO FINAL</span>
          <p>Encerre a partida para abrir a votação de destaque.</p>
          <Link className="button button-primary" href="/partidas/ao-vivo">Ir para central da partida</Link>
        </div>
      );
    }

    return (
      <div className="game-demo-panel">
        <section aria-label={`Escolher destaque de ${match.title}`} className="player-vote-list">
          {checkedIn.map((player) => {
            const selected = playerOfTheMatch === player;
            const goalsByPlayer = goals.filter((goal) => goal.player === player).length;

            return (
              <article className={`player-vote-card${selected ? " is-selected" : ""}`} key={player}>
                <div>
                  <strong>{player}</strong>
                  <span>{goalsByPlayer} {goalsByPlayer === 1 ? "gol" : "gols"} na prévia</span>
                </div>
                <button
                  aria-pressed={selected}
                  className={`button ${selected ? "button-secondary" : "button-primary"}`}
                  onClick={() => {
                    if (voteForPlayer(player)) setNotice(`Destaque da prévia: ${player}.`);
                  }}
                  type="button"
                >
                  {selected ? "Destaque escolhido" : "Escolher destaque"}
                </button>
              </article>
            );
          })}
        </section>
        {notice || playerOfTheMatch ? (
          <p aria-live="polite" className="form-feedback" role="status">
            {notice || `Destaque atual da prévia: ${playerOfTheMatch}.`}
          </p>
        ) : null}
        <div className="match-demo-links">
          <Link href="/partidas/ao-vivo">Voltar à central da partida</Link>
          <span>Votação demonstrativa: a escolha fica apenas nesta sessão.</span>
        </div>
      </div>
    );
  }

  const greenScore = goals.filter((goal) => goal.side === "green").length;
  const blueScore = goals.filter((goal) => goal.side === "blue").length;
  const readyToStart = checkedIn.length >= 2;

  function handleGoal(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const formData = new FormData(event.currentTarget);
    const player = String(formData.get("goal-scorer"));
    const side = String(formData.get("goal-side"));
    if (side !== "green" && side !== "blue") return;
    if (!recordGoal(player, side)) {
      setNotice("Registre o gol durante a partida e selecione alguém com check-in.");
      return;
    }

    setNotice(`Gol registrado para o ${side === "green" ? "Time Verde" : "Time Azul"} — ${player}.`);
    event.currentTarget.reset();
  }

  return (
    <div className="game-demo-panel">
      <div className={`match-status-banner match-status-${status}`} role="status">
        <span className="status-dot" />
        {status === "scheduled" ? "Pronta para começar" : status === "live" ? "Partida em andamento" : "Partida encerrada"}
      </div>
      <section aria-label={`Placar de ${match.title}`} className="game-scoreboard">
        <div className="game-score-team game-score-green">
          <span>TIME VERDE</span>
          <strong>{greenScore}</strong>
        </div>
        <span aria-hidden="true" className="game-score-divider">×</span>
        <div className="game-score-team game-score-blue">
          <span>TIME AZUL</span>
          <strong>{blueScore}</strong>
        </div>
      </section>

      {status === "scheduled" ? (
        <section className="game-state-card">
          <div>
            <strong>Antes do apito inicial</strong>
            <span>{checkedIn.length} atletas com check-in · mínimo de 2 para começar</span>
          </div>
          <button
            className="button button-primary"
            disabled={!readyToStart}
            onClick={() => {
              if (startMatch()) setNotice("Partida iniciada. Os lances já podem ser registrados.");
            }}
            type="button"
          >
            Iniciar partida
          </button>
        </section>
      ) : null}

      {status === "live" ? (
        <>
          <form className="game-goal-form" onSubmit={handleGoal}>
            <label className="preview-field" htmlFor="goal-scorer">
              <span>Quem marcou?</span>
              <select defaultValue="" id="goal-scorer" name="goal-scorer" required>
                <option disabled value="">Selecione quem fez check-in</option>
                {checkedIn.map((player) => <option key={player} value={player}>{player}</option>)}
              </select>
            </label>
            <label className="preview-field" htmlFor="goal-side">
              <span>Time</span>
              <select defaultValue="" id="goal-side" name="goal-side" required>
                <option disabled value="">Selecione o time</option>
                <option value="green">Time Verde</option>
                <option value="blue">Time Azul</option>
              </select>
            </label>
            <button className="button button-primary" type="submit">Registrar gol</button>
          </form>
          {confirmEnding ? (
            <section aria-label="Confirmar encerramento" className="game-state-card game-end-confirm">
              <div>
                <strong>Encerrar esta partida?</strong>
                <span>O placar ficará fechado nesta prévia e a votação de destaque será liberada.</span>
              </div>
              <div aria-describedby="end-match-description" aria-labelledby="end-match-title" className="game-state-actions" role="group">
                <strong className="sr-only" id="end-match-title">Confirmar encerramento</strong>
                <button className="button button-secondary" onClick={() => setConfirmEnding(false)} ref={continueButtonRef} type="button">Continuar jogando</button>
                <button
                  className="button button-primary"
                  onClick={() => {
                    if (finishMatch()) {
                      setConfirmEnding(false);
                      setNotice("Partida encerrada. O resultado segue como prévia, aguardando conferência.");
                    }
                  }}
                  type="button"
                >
                  Confirmar encerramento
                </button>
              </div>
              <span className="sr-only" id="end-match-description">
                O placar será fechado nesta prévia e a votação de destaque será liberada.
              </span>
            </section>
          ) : (
            <button className="button button-secondary game-end-button" onClick={() => setConfirmEnding(true)} ref={endMatchButtonRef} type="button">
              Encerrar partida
            </button>
          )}
        </>
      ) : null}

      {status === "completed" ? (
        <section aria-label="Resultado final demonstrativo" className="game-state-card game-final-result">
          <div>
            <strong>Resultado final para conferência</strong>
            <span>Este resultado é ilustrativo e não foi enviado para aprovação.</span>
          </div>
          <Link className="button button-primary" href="/destaques/votacao" ref={voteHighlightLinkRef}>Votar destaque</Link>
        </section>
      ) : null}

      {notice ? <p aria-live="polite" className="form-feedback" role="status">{notice}</p> : null}

      <section aria-label="Lances registrados" className="game-event-list">
        <h3>Lances da partida</h3>
        {goals.length ? [...goals].reverse().map((goal: MatchGoal) => (
          <article className="game-event-row" key={goal.id}>
            <span className={`game-event-indicator game-event-${goal.side}`} aria-hidden="true" />
            <span><strong>Gol</strong><small>{goal.side === "green" ? "Time Verde" : "Time Azul"}</small></span>
            <strong>{goal.player}</strong>
            {status === "live" ? (
              <button aria-label={`Desfazer gol de ${goal.player}`} className="game-event-undo" onClick={() => removeGoal(goal.id)} type="button">
                Desfazer
              </button>
            ) : null}
          </article>
        )) : (
          <p className="directory-empty">Nenhum lance registrado nesta prévia.</p>
        )}
      </section>

      <div className="match-demo-links">
        <Link href="/presenca/check-in">Gerenciar check-in</Link>
        {status === "completed" ? <Link href="/destaques/votacao">Escolher destaque</Link> : null}
        <span>Placar e lances existem somente nesta sessão de demonstração.</span>
      </div>
    </div>
  );
}
