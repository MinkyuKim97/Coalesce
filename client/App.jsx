import { useEffect, useState } from "react";
import { db } from "../firebaseConfig.js";
import {
  collection,
  doc,
  getDocs,
  onSnapshot,
  query,
  setDoc,
  updateDoc,
  where,
} from "firebase/firestore";

import "/style.css";

const APP_TITLE = "Coalesce";

function isReservedClientId(id) {
  const s = String(id || "");
  return s === "0000" || s.startsWith("0000");
}


// ------------------------------------------------------------------
// ------------------------------------------------------------------
// ------------------------------------------------------------------
// Utilities
function normalizeSpaces(s) {
  return (s || "").trim().replace(/\s+/g, " ");
}
function buildFullName(first, last) {
  const f = normalizeSpaces(first);
  const l = normalizeSpaces(last);
  if (!f && !l) return "";
  if (!l) return f;
  if (!f) return l;
  return `${f} ${l}`;
}
function normalizeNameForCompare(name) {
  return normalizeSpaces(name).toLowerCase();
}
function normalizeEmailForCompare(email) {
  return normalizeSpaces(email).toLowerCase();
}
function isValidEmailBasic(email) {
  const e = normalizeSpaces(email);
  return !!e && e.includes("@") && e.includes(".");
}
function pad4(n) {
  const s = String(n);
  return s.padStart(4, "0").slice(-4);
}
function safeIntFromAny(v, fallback = 0) {
  const n = parseInt(String(v ?? ""), 10);
  return Number.isFinite(n) ? n : fallback;
}
function toClientShape(data) {
  const d = data || {};
  return {
    Name: String(d.Name || ""),
    Email: String(d.Email || ""),
    Website: String(d.Website || ""),
    LinkedIn: String(d.LinkedIn || ""),
    Instagram: String(d.Instagram || ""),
  };
}
function nonEmptyUrl(v) {
  const s = normalizeSpaces(v);
  return s.length ? s : "";
}


function buildUnlockPlan(otherClient) {
  const website = nonEmptyUrl(otherClient?.Website);
  const linkedin = nonEmptyUrl(otherClient?.LinkedIn);
  const instagram = nonEmptyUrl(otherClient?.Instagram);

  const contactKeys = [];
  let step = 1;

  if (website) contactKeys.push({ key: "Website", value: website, threshold: step++ });
  if (linkedin) contactKeys.push({ key: "LinkedIn", value: linkedin, threshold: step++ });
  if (instagram) contactKeys.push({ key: "Instagram", value: instagram, threshold: step++ });

  contactKeys.push({
  key: "Invite Dinner",
  value: "",
  threshold: 10,
  isInviteDinner: true,
});

  return contactKeys;
}

async function allocateNextClientId() {
  const snap = await getDocs(collection(db, "clients"));
  const used = new Set();
  snap.forEach((d) => used.add(d.id));

  for (let i = 1; i <= 9999; i++) {
    const id = pad4(i);
    if (!used.has(id)) return id;
  }
  throw new Error("");
}

async function findClientByNameEmail(fullName, email) {
  const nameKey = normalizeNameForCompare(fullName);
  const emailKey = normalizeEmailForCompare(email);

  const q1 = query(collection(db, "clients"), where("Email", "==", emailKey));
  const snap = await getDocs(q1);

  for (const d of snap.docs) {
    if (isReservedClientId(d.id)) continue;

    const data = d.data() || {};
    const n = normalizeNameForCompare(data?.Name || "");
    const e = normalizeEmailForCompare(data?.Email || "");
    if (n === nameKey && e === emailKey) return { id: d.id, ...data };
  }
  return null;
}

function yyyymmddLocal(date) {
  const y = date.getFullYear();
  const m = String(date.getMonth() + 1).padStart(2, "0");
  const d = String(date.getDate()).padStart(2, "0");
  return `${y}${m}${d}`;
}

function buildDinnerInviteUrl({ guestEmail, tz = "America/New_York" }) {
  const now = new Date(); // 사용자 로컬 "오늘"
  const day = yyyymmddLocal(now);

  const start = `${day}T180000`;
  const end = `${day}T190000`;

  const url = new URL("https://calendar.google.com/calendar/render");
  url.searchParams.set("action", "TEMPLATE");
  url.searchParams.set("text", "Dinner Together");
  url.searchParams.set("dates", `${start}/${end}`);
  url.searchParams.set("ctz", tz);


  const e = (guestEmail || "").trim();
  if (e) url.searchParams.append("add", e);

  return url.toString();
}

// ------------------------------------------------------------------
// ------------------------------------------------------------------
// ------------------------------------------------------------------
// UI related functions
function Divider() {
  return <div className="divider" />;
}

function FieldRow({ label, value, onChange, placeholder, type = "text", disabled }) {
  return (
    <label className="fieldRow">
      <div className="fieldLabel">{label}</div>
      <input
        className="fieldInput"
        type={type}
        value={value}
        disabled={disabled}
        placeholder={placeholder}
        onChange={(e) => onChange(e.target.value)}
      />
    </label>
  );
}

function Button({ children, onClick, disabled, variant = "primary", type = "button" }) {
  const cls =
    variant === "danger"
      ? "btn btnDanger"
      : variant === "ghost"
      ? "btn btnGhost"
      : "btn btnPrimary";

  return (
    <button type={type} onClick={onClick} disabled={disabled} className={cls}>
      {children}
    </button>
  );
}

function SmallPill({ children }) {
  return <div className="pill">{children}</div>;
}



function UnlockCard({ title, unlocked, url, isCalendar }) {
  const clickable = unlocked && !isCalendar && !!url;

  function handleClick() {
    if (!clickable) return;
    window.open(url, "_blank", "noopener,noreferrer");
  }

  return (
    <div
      role={clickable ? "button" : undefined}
      tabIndex={clickable ? 0 : -1}
      onClick={handleClick}
      onKeyDown={(e) => {
        if (!clickable) return;
        if (e.key === "Enter" || e.key === " ") handleClick();
      }}
      style={{
        border: unlocked
          ? "var(--strokeThick) solid var(--whiteFirst)"
          : "var(--strokeThin) solid var(--whiteSecond)",
        background: unlocked
          ? "var(--blackFirst)"
          : "var(--blackSecond)",
        padding: "10px",
        marginTop: 10,
        cursor: clickable ? "pointer" : "default",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        textAlign: "center",
        fontSize: unlocked
          ? "16px"
          : "12px",
        fontWeight: unlocked
          ? 700
          : 500,
        letterSpacing: 0.2,
        color: unlocked ? "rgba(255,255,255,0.95)" : "rgba(255,255,255,0.35)",
        userSelect: "none",
        transition: "border 120ms ease, color 120ms ease",
      }}
    >
      {title}
    </div>
  );
}




// ------------------------------------------------------------------
// ------------------------------------------------------------------
// ------------------------------------------------------------------


function AuthScreen({
  firstName,
  lastName,
  email,
  setFirstName,
  setLastName,
  setEmail,
  onSubmit,
  authBusy,
  authError,
}) {
  return (
    <div className="totalBox">
      <div className="card">
        <div className="topBar">
          <div className="appTitle">{APP_TITLE}</div>
        </div>

        <div className="subText">Fill the information to continue</div>
        <div className="subText">(If it's your first time using, submit the information to register)</div>

        <FieldRow label="First Name" value={firstName} onChange={setFirstName} />
        <FieldRow label="Last Name" value={lastName} onChange={setLastName} />
        <FieldRow label="Email" value={email} onChange={setEmail}/>

        <Button onClick={onSubmit} disabled={authBusy}>
          Confirm
        </Button>

        {/* {authError ? <div className="errorText">{authError}</div> : null} */}

      </div>
    </div>
  );
}

function ProfileScreen({
  myName,
  myEmail,
  website,
  linkedin,
  instagram,
  setWebsite,
  setLinkedin,
  setInstagram,
  lockWebsite,
  lockLinkedin,
  lockInstagram,
  setLockWebsite,
  setLockLinkedin,
  setLockInstagram,
  onSubmit,
  onLogout,
  profileBusy,
  profileError,
}) {
  return (
    <div className="totalBox">
      <div className="card">
        <div className="topBar">
          <div className="appTitleSmall">{APP_TITLE}</div>
          <button onClick={onLogout} className="pillBtn">
            Logout
          </button>
        </div>

        <div className="currentClientBlock">
          <div className="currentClientName">{myName || "—"}</div>
          <div className="currentClientEmail">{myEmail || "—"}</div>
        </div>

        <Divider />

        <div className="sectionDetail">Contact Info Setup</div>
        <div className="sectionDetail">Copy/Paste your contact urls</div>

        {/* Website */}
        <div className="contactBox">
          <div className="contactBoxHeader">
            <div className="contactBoxTitle">Website</div>
            <label className="lockLabel">
              <input type="checkbox" checked={lockWebsite} onChange={(e) => setLockWebsite(e.target.checked)} />
              <span>Check if you don't want to use</span>
            </label>
          </div>
          <input
            className="fieldInput"
            value={website}
            onChange={(e) => setWebsite(e.target.value)}
          />
        </div>

        {/* LinkedIn */}
        <div className="contactBox">
          <div className="contactBoxHeader">
            <div className="contactBoxTitle">LinkedIn</div>
            <label className="lockLabel">
              <input type="checkbox" checked={lockLinkedin} onChange={(e) => setLockLinkedin(e.target.checked)} />
              <span>Check if you don't want to use</span>
            </label>
          </div>
          <input
            className="fieldInput"
            value={linkedin}
            onChange={(e) => setLinkedin(e.target.value)}
          />
        </div>

        {/* Instagram */}
        <div className="contactBox">
          <div className="contactBoxHeader">
            <div className="contactBoxTitle">Instagram</div>
            <label className="lockLabel">
              <input type="checkbox" checked={lockInstagram} onChange={(e) => setLockInstagram(e.target.checked)} />
              <span>Check if you don't want to use</span>
            </label>
          </div>
          <input
            className="fieldInput"
            value={instagram}
            onChange={(e) => setInstagram(e.target.value)}
          />
        </div>

        <Button onClick={onSubmit} disabled={profileBusy}>
        Submit
        </Button>

        {/* {profileError ? <div className="errorText">{profileError}</div> : null} */}
      </div>
    </div>
  );
}

function PersonalScreen({ myName, myEmail, connCount, totalOthers, connections, othersMap, onLogout }) {
  return (
    <div className="totalBox">
      <div className="card">
        <div className="meTop">
          <div className="meTopLeft">
            <div className="meName">{myName || "—"}</div>
          </div>

          <div className="meTopRight">
            <SmallPill>{`How many you met?: ${connCount}/${totalOthers || 0}`}</SmallPill>
            <button onClick={onLogout} className="pillBtn">
              Logout
            </button>
          </div>
        </div>


        <Divider />

          <a
            href="https://evilpotatoking.itch.io/handshake-test"
            target="_blank"
            rel="noopener noreferrer"
            style={{ textDecoration: "none", display: "block" }}
          >
            <Button>
              Coalescence, Data Visualization
            </Button>
          </a>

        <Divider />
        <Divider />

        <div className="sectionTitle">Connections</div>

        {connections.length === 0 ? (
          <div className="muted"></div>
        ) : (
          <div>
            {connections.map((c) => {
              const other = othersMap[c.otherId];
              const otherName = other?.Name || c.otherId;
              
              const stateNum = Math.max(0, safeIntFromAny(c.state, 0));
              const plan = buildUnlockPlan(other);
              const guestEmail = other?.Email || "";
              return (
                <div key={c.otherId} className="connCard">
                  <div className="connHeader">
                    <div className="connName">{otherName}</div>
                  </div>

                  {plan.map((item) => {
                    const unlocked = stateNum >= item.threshold;

                    
                    if (item.isInviteDinner) {
                      const inviteUrl = buildDinnerInviteUrl({
                        guestEmail,
                        tz: "America/New_York",
                      });

                      return (
                        <UnlockCard
                          key={`${c.otherId}-${item.key}`}
                          title={item.key}
                          unlocked={unlocked}
                          url={inviteUrl}
                          isCalendar={false}
                        />
                      );
                    }

                    
                    return (
                      <UnlockCard
                        key={`${c.otherId}-${item.key}`}
                        title={item.key}
                        unlocked={unlocked}
                        url={item.value}
                        isCalendar={false}
                      />
                    );
                  })}
                </div>
              );
            })}
          </div>
        )}
      </div>
    </div>
  );
}

// ------------------------------------------------------------------
// ------------------------------------------------------------------
// ------------------------------------------------------------------
// ------------------------------------------------------------------

// Main App render

export function App() {
  const [screen, setScreen] = useState("auth");

  // Auth inputs
  const [firstName, setFirstName] = useState("");
  const [lastName, setLastName] = useState("");
  const [email, setEmail] = useState("");
  const [authError, setAuthError] = useState("");
  const [authBusy, setAuthBusy] = useState(false);

  // Logged in client
  const [myClientId, setMyClientId] = useState("");
  const [myClient, setMyClient] = useState(null);

  // Profile inputs + locks
  const [website, setWebsite] = useState("");
  const [linkedin, setLinkedin] = useState("");
  const [instagram, setInstagram] = useState("");

  const [lockWebsite, setLockWebsite] = useState(false);
  const [lockLinkedin, setLockLinkedin] = useState(false);
  const [lockInstagram, setLockInstagram] = useState(false);

  const [profileBusy, setProfileBusy] = useState(false);
  const [profileError, setProfileError] = useState("");


  const [connCount, setConnCount] = useState(0);
  const [connections, setConnections] = useState([]); // [{otherId, state}]
  const [othersMap, setOthersMap] = useState({});
  const [totalOthers, setTotalOthers] = useState(0); // ✅ 분모용


  useEffect(() => {
    document.title = APP_TITLE;
    const savedId = localStorage.getItem("coalesce_clientId") || "";
    if (savedId) {
      setMyClientId(savedId);
      setScreen("me");
    }
  }, []);


  useEffect(() => {
    if (!myClientId) return;

    const unsub = onSnapshot(
      doc(db, "clients", myClientId),
      (snap) => {
        if (!snap.exists()) {
          setMyClient(null);
          return;
        }
        setMyClient({ id: snap.id, ...toClientShape(snap.data()) });
      },
      (err) => console.error("my client snapshot error:", err)
    );

    return () => unsub();
  }, [myClientId]);


  useEffect(() => {
    if (!myClientId) {
      setTotalOthers(0);
      return;
    }

    const unsub = onSnapshot(
      collection(db, "clients"),
      (snap) => {
        let count = 0;
        snap.forEach((d) => {
          if (isReservedClientId(d.id)) return;
          if (d.id === myClientId) return;
          count++;
        });
        setTotalOthers(count);
      },
      (err) => console.error("clients snapshot error:", err)
    );

    return () => unsub();
  }, [myClientId]);


  useEffect(() => {
    if (!myClientId) return;

    const colRef = collection(db, "clients", myClientId, "clientConnection");
    const unsub = onSnapshot(
      colRef,
      (snap) => {
        const list = [];
        snap.forEach((d) => {
          if (isReservedClientId(d.id)) return;
          const data = d.data() || {};
          list.push({ otherId: d.id, state: String(data.State ?? "0") });
        });
        list.sort((a, b) => a.otherId.localeCompare(b.otherId));
        setConnections(list);
        setConnCount(list.length);
      },
      (err) => console.error("clientConnection snapshot error:", err)
    );

    return () => unsub();
  }, [myClientId]);


  useEffect(() => {
    const unsubs = [];
    let alive = true;

    const ids = connections.map((c) => c.otherId).filter((id) => id && !isReservedClientId(id));
    if (!ids.length) {
      setOthersMap({});
      return () => {};
    }

   setOthersMap((prev) => {
      const next = { ...prev };
      for (const k of Object.keys(next)) {
        if (!ids.includes(k)) delete next[k];
      }
      return next;
    });

    for (const id of ids) {
      const unsub = onSnapshot(
        doc(db, "clients", id),
        (snap) => {
          if (!alive) return;
          if (!snap.exists()) {
            setOthersMap((prev) => {
              const next = { ...prev };
              delete next[id];
              return next;
            });
            return;
          }
          setOthersMap((prev) => ({ ...prev, [id]: { id: snap.id, ...toClientShape(snap.data()) } }));
        },
        (err) => console.error("other client snapshot error:", id, err)
      );
      unsubs.push(unsub);
    }

    return () => {
      alive = false;
      unsubs.forEach((fn) => {
        try { fn(); } catch {}
      });
    };
  }, [connections]);


  

  async function handleAuthSubmit() {
    setAuthError("");
    setProfileError("");

    const fullName = buildFullName(firstName, lastName);
    const emailClean = normalizeEmailForCompare(email);

    if (!fullName || !emailClean) return setAuthError("Fill Infos");
    if (!isValidEmailBasic(emailClean)) return setAuthError("Fill Infos");

    setAuthBusy(true);
    try {
      const existing = await findClientByNameEmail(fullName, emailClean);
      if (existing?.id) {
        localStorage.setItem("coalesce_clientId", existing.id);
        setMyClientId(existing.id);
        setScreen("me");
        return;
      }

      const newId = await allocateNextClientId();
      const docData = {
        Name: normalizeSpaces(fullName),
        Email: emailClean,
        Website: "",
        LinkedIn: "",
        Instagram: "",
      };

      await setDoc(doc(db, "clients", newId), docData);

      // auto create clientConnection + dummy
      await setDoc(doc(db, "clients", newId, "clientConnection", "0000"), { State: "0" });

      localStorage.setItem("coalesce_clientId", newId);
      setMyClientId(newId);

      setWebsite("");
      setLinkedin("");
      setInstagram("");
      setLockWebsite(false);
      setLockLinkedin(false);
      setLockInstagram(false);

      setScreen("profile");
    } catch (e) {
      console.error("auth failed:", e);
      setAuthError(e?.message || "Auth failed.");
    } finally {
      setAuthBusy(false);
    }
  }

  async function handleProfileSubmit() {
    if (!myClientId || !myClient) return;

    setProfileError("");
    setProfileBusy(true);
    try {
      const w = normalizeSpaces(website);
      const l = normalizeSpaces(linkedin);
      const i = normalizeSpaces(instagram);

      if (!lockWebsite && !w) return setProfileError("Fill infos");
      if (!lockLinkedin && !l) return setProfileError("Fill infos");
      if (!lockInstagram && !i) return setProfileError("Fill infos");

      await updateDoc(doc(db, "clients", myClientId), {
        Website: w || "",
        LinkedIn: l || "",
        Instagram: i || "",
      });

      setScreen("me");
    } catch (e) {
      console.error("profile submit failed:", e);
      setProfileError(e?.message || "Failed to update profile.");
    } finally {
      setProfileBusy(false);
    }
  }

  function handleLogout() {
    localStorage.removeItem("coalesce_clientId");
    setMyClientId("");
    setMyClient(null);
    setConnections([]);
    setOthersMap({});
    setConnCount(0);
    setTotalOthers(0);
    setScreen("auth");
    setAuthError("");
    setProfileError("");
  }


  const myName = myClient?.Name || "";
  const myEmail = myClient?.Email || "";


  if (screen === "auth") {
    return (
      <AuthScreen
        firstName={firstName}
        lastName={lastName}
        email={email}
        setFirstName={setFirstName}
        setLastName={setLastName}
        setEmail={setEmail}
        onSubmit={handleAuthSubmit}
        authBusy={authBusy}
        authError={authError}
      />
    );
  }

  if (screen === "profile") {
    return (
      <ProfileScreen
        myName={myName}
        myEmail={myEmail}
        website={website}
        linkedin={linkedin}
        instagram={instagram}
        setWebsite={setWebsite}
        setLinkedin={setLinkedin}
        setInstagram={setInstagram}
        lockWebsite={lockWebsite}
        lockLinkedin={lockLinkedin}
        lockInstagram={lockInstagram}
        setLockWebsite={setLockWebsite}
        setLockLinkedin={setLockLinkedin}
        setLockInstagram={setLockInstagram}
        onSubmit={handleProfileSubmit}
        onLogout={handleLogout}
        profileBusy={profileBusy}
        profileError={profileError}
      />
    );
  }

  return (
    <PersonalScreen
      myName={myName}
      myEmail={myEmail}
      connCount={connCount}
      totalOthers={totalOthers}
      connections={connections}
      othersMap={othersMap}
      onLogout={handleLogout}
    />
  );
}