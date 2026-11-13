import Link from "next/link";
import { AppShell } from "@/components/app-shell";
import { Icon } from "@/components/icon";

const posts = [
  {
    initials: "LC",
    name: "Lucas Costa",
    time: "há 2 horas",
    color: "mint",
    message: "Quinta tá chegando. Já deixei a chuteira separada — quem mais vai?",
    tag: "Quinta de futebol",
    likes: "12 curtidas",
    comments: "4 comentários",
    image: "post-image-green",
    imageLabel: "Campo de futebol ao entardecer",
  },
  {
    initials: "BA",
    name: "Bruno Alves",
    time: "ontem",
    color: "lavender",
    message: "Domingo foi daqueles. Valeu demais, time! Próxima já está na agenda.",
    tag: "Desafio de domingo",
    likes: "8 curtidas",
    comments: "2 comentários",
    image: "post-image-blue",
    imageLabel: "Bola de futebol no gramado",
  },
];

export default function CommunityPage() {
  return (
    <AppShell active="community">
      <div className="module-page community-page">
        <section className="page-heading">
          <div>
            <p className="eyebrow page-eyebrow">SANTA CECÍLIA FC · COMUNIDADE</p>
            <h1>A resenha continua por aqui.</h1>
            <p className="page-subtitle">Histórias, encontros e tudo que acontece dentro e fora de campo.</p>
          </div>
          <Link className="button button-primary" href="/peladas">
            <Icon name="football" />
            Ver próximas peladas
          </Link>
        </section>

        <div className="community-layout">
          <section aria-label="Publicações recentes" className="community-feed">
            {posts.map((post) => (
              <article className="surface-card post-card" key={post.name}>
                <div className="post-author">
                  <span className={`avatar avatar-soft avatar-${post.color}`}>{post.initials}</span>
                  <span><strong>{post.name}</strong><span>{post.time} <span className="dot-separator">·</span> Santa Cecília FC</span></span>
                  <span className="post-menu" aria-hidden="true">...</span>
                </div>
                <p className="post-message">{post.message}</p>
                <div aria-label={post.imageLabel} className={`post-image ${post.image}`} role="img">
                  <span className="post-image-mark"><Icon name="football" /></span>
                  <span className="post-image-caption">SANTA CECÍLIA FC</span>
                </div>
                <div className="post-tag"><Icon name="calendar" /> {post.tag}</div>
                <div className="post-actions">
                  <span><Icon name="sparkles" /> {post.likes}</span>
                  <span>{post.comments}</span>
                </div>
              </article>
            ))}
          </section>

          <aside className="community-aside">
            <article className="surface-card community-info">
              <p className="eyebrow">SEU GRUPO</p>
              <div className="community-group-mark">SC</div>
              <h2>Santa Cecília FC</h2>
              <p>Amigos, futebol e boas histórias desde sempre.</p>
              <div className="group-facts">
                <span><Icon name="people" /> 24 jogadores</span>
                <span><Icon name="map-pin" /> São Paulo, SP</span>
              </div>
            </article>
            <article className="community-invite">
              <span className="stat-icon stat-icon-yellow"><Icon name="sparkles" /></span>
              <h2>O próximo capítulo é no campo.</h2>
              <p>Reúna o time para a próxima pelada.</p>
              <Link className="text-link" href="/peladas">Abrir agenda <Icon name="arrow-right" /></Link>
            </article>
          </aside>
        </div>
        <p className="demo-note">Publicações e interações são exemplos visuais. O feed social será conectado à API em uma próxima etapa.</p>
      </div>
    </AppShell>
  );
}
