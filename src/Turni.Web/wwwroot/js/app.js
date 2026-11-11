// Frontend senza librerie: fetch verso l'API, DOM costruito con createElement.
// Uso textContent e mai innerHTML con dati del server: un nome come "<img onerror=...>" resta solo testo.

const GIORNI = ["Lun", "Mar", "Mer", "Gio", "Ven", "Sab", "Dom"];
const formatoTitolo = new Intl.DateTimeFormat("it-IT", { day: "numeric", month: "long" });

const stato = {
  lunedi: lunediDi(new Date()),
  dipendenti: [],
};

const $ = (id) => document.getElementById(id);
const finestra = $("finestra-turno");
const modulo = $("modulo-turno");

// ---------- Date (tutte in ora locale, come i turni) ----------

function lunediDi(data) {
  const giorno = new Date(data.getFullYear(), data.getMonth(), data.getDate());
  giorno.setDate(giorno.getDate() - ((giorno.getDay() + 6) % 7)); // getDay(): domenica = 0
  return giorno;
}

function aggiungiGiorni(data, giorni) {
  const copia = new Date(data);
  copia.setDate(copia.getDate() + giorni);
  return copia;
}

function isoData(data) {
  const due = (n) => String(n).padStart(2, "0");
  return `${data.getFullYear()}-${due(data.getMonth() + 1)}-${due(data.getDate())}`;
}

const ora = (isoDataOra) => isoDataOra.slice(11, 16);

function fascia(isoDataOra) {
  const h = Number(isoDataOra.slice(11, 13));
  if (h < 12) return "mattina";
  if (h < 18) return "pomeriggio";
  return "sera";
}

// ---------- Chiamate all'API ----------

async function api(percorso, opzioni = {}) {
  try {
    const risposta = await fetch(`/api${percorso}`, {
      headers: { "Content-Type": "application/json" },
      ...opzioni,
    });
    const corpo = risposta.status === 204 ? null : await risposta.json().catch(() => null);
    return { ok: risposta.ok, status: risposta.status, corpo };
  } catch {
    return { ok: false, status: 0, corpo: { errori: ["Server non raggiungibile. È avviato?"] } };
  }
}

// ---------- Disegno della pagina ----------

function el(tag, attributi = {}, ...figli) {
  const elemento = document.createElement(tag);
  for (const [nome, valore] of Object.entries(attributi)) {
    if (nome === "class") elemento.className = valore;
    else if (nome.startsWith("on")) elemento.addEventListener(nome.slice(2), valore);
    else elemento.setAttribute(nome, valore);
  }
  elemento.append(...figli);
  return elemento;
}

function mostraMessaggio(testo, tipo = "ok") {
  const messaggio = $("messaggio");
  messaggio.textContent = testo;
  messaggio.className = `messaggio ${tipo}`;
  messaggio.hidden = false;
}

function disegna(settimana) {
  const giorni = GIORNI.map((_, i) => aggiungiGiorni(stato.lunedi, i));
  const oggi = isoData(new Date());
  const domenica = giorni[6];
  $("titolo-settimana").textContent =
    `${formatoTitolo.format(stato.lunedi)} – ${formatoTitolo.format(domenica)} ${domenica.getFullYear()}`;

  const intestazione = el("tr", {}, el("th", { scope: "col" }, "Dipendente"));
  giorni.forEach((giorno, i) => {
    intestazione.append(el("th", { scope: "col", class: isoData(giorno) === oggi ? "oggi" : "" },
      `${GIORNI[i]} ${giorno.getDate()}/${giorno.getMonth() + 1}`));
  });
  $("intestazione").replaceChildren(intestazione);

  const righe = stato.dipendenti.map((dipendente) => {
    const ore = settimana.ore.find((o) => o.dipendenteId === dipendente.id)?.ore ?? 0;
    const percentuale = Math.min(100, (ore / dipendente.oreSettimanali) * 100);
    const barra = el("span");
    barra.style.setProperty("--percentuale", `${percentuale}%`);

    const riga = el("tr", {},
      el("th", { scope: "row", class: "dipendente" },
        el("strong", {}, dipendente.nome),
        el("span", { class: "ruolo" }, dipendente.ruolo),
        el("div", { class: ore > dipendente.oreSettimanali ? "ore straordinari" : "ore" },
          `${ore.toLocaleString("it-IT")} / ${dipendente.oreSettimanali} ore`,
          el("div", { class: "barra" }, barra))));

    for (const giorno of giorni) {
      const data = isoData(giorno);
      const cella = el("td", { class: data === oggi ? "oggi" : "" });
      settimana.turni
        .filter((t) => t.dipendenteId === dipendente.id && t.inizio.startsWith(data))
        .forEach((t) => cella.append(el("div", { class: `turno ${fascia(t.inizio)}` },
          `${ora(t.inizio)}–${ora(t.fine)}`,
          el("button", {
            type: "button",
            "aria-label": `Elimina il turno di ${dipendente.nome} delle ${ora(t.inizio)}`,
            onclick: () => elimina(t),
          }, "×"))));
      cella.append(el("button", {
        type: "button",
        class: "aggiungi",
        "aria-label": `Aggiungi un turno a ${dipendente.nome} il ${giorno.getDate()}/${giorno.getMonth() + 1}`,
        onclick: () => apriFinestra(dipendente.id, data),
      }, "+"));
      riga.append(cella);
    }
    return riga;
  });
  $("corpo").replaceChildren(...righe);
}

async function carica() {
  const risposta = await api(`/turni?settimana=${isoData(stato.lunedi)}`);
  if (!risposta.ok) {
    mostraMessaggio(risposta.corpo?.errori?.[0] ?? "Impossibile caricare i turni.", "errore");
    return;
  }
  disegna(risposta.corpo);
}

// ---------- Azioni ----------

function apriFinestra(dipendenteId, giorno) {
  modulo.dipendenteId.value = dipendenteId ?? stato.dipendenti[0]?.id;
  modulo.giorno.value = giorno ?? isoData(new Date());
  $("errori").replaceChildren();
  aggiornaNotaNotte();
  finestra.showModal();
}

// Se la fine è prima dell'inizio (es. 19:00 - 02:00) il turno finisce il giorno dopo
const finisceDomani = () => modulo.fine.value <= modulo.inizio.value;

function aggiornaNotaNotte() {
  $("nota-notte").hidden = !finisceDomani();
}

async function salva(evento) {
  evento.preventDefault();
  const giorno = modulo.giorno.value;
  const giornoFine = finisceDomani() ? isoData(aggiungiGiorni(new Date(`${giorno}T00:00`), 1)) : giorno;
  const turno = {
    dipendenteId: Number(modulo.dipendenteId.value),
    inizio: `${giorno}T${modulo.inizio.value}:00`,
    fine: `${giornoFine}T${modulo.fine.value}:00`,
  };

  const risposta = await api("/turni", { method: "POST", body: JSON.stringify(turno) });
  if (!risposta.ok) {
    const errori = risposta.corpo?.errori ?? [`Errore ${risposta.status}.`];
    $("errori").replaceChildren(...errori.map((e) => el("li", {}, e)));
    return;
  }

  finestra.close();
  const avvisi = risposta.corpo.avvisi;
  mostraMessaggio(avvisi.length ? avvisi.join(" ") : "Turno salvato.", avvisi.length ? "avviso" : "ok");
  stato.lunedi = lunediDi(new Date(`${giorno}T00:00`)); // mostro la settimana del turno appena creato
  await carica();
}

async function elimina(turno) {
  if (!confirm(`Eliminare il turno ${ora(turno.inizio)}–${ora(turno.fine)}?`)) return;
  const risposta = await api(`/turni/${turno.id}`, { method: "DELETE" });
  mostraMessaggio(risposta.ok ? "Turno eliminato." : "Il turno non esiste più.", risposta.ok ? "ok" : "avviso");
  await carica();
}

function cambiaSettimana(giorni) {
  stato.lunedi = aggiungiGiorni(stato.lunedi, giorni);
  carica();
}

// ---------- Avvio ----------

async function avvia() {
  $("precedente").addEventListener("click", () => cambiaSettimana(-7));
  $("successiva").addEventListener("click", () => cambiaSettimana(7));
  $("oggi").addEventListener("click", () => {
    stato.lunedi = lunediDi(new Date());
    carica();
  });
  $("nuovo-turno").addEventListener("click", () => apriFinestra());
  $("annulla").addEventListener("click", () => finestra.close());
  modulo.addEventListener("submit", salva);
  modulo.inizio.addEventListener("input", aggiornaNotaNotte);
  modulo.fine.addEventListener("input", aggiornaNotaNotte);

  const risposta = await api("/dipendenti");
  if (!risposta.ok) {
    mostraMessaggio(risposta.corpo?.errori?.[0] ?? "Impossibile caricare la squadra.", "errore");
    return;
  }
  stato.dipendenti = risposta.corpo;
  modulo.dipendenteId.replaceChildren(
    ...stato.dipendenti.map((d) => el("option", { value: d.id }, `${d.nome} (${d.ruolo})`)));
  await carica();
}

avvia();
