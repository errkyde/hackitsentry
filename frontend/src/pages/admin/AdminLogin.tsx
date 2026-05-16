import { useState } from "react";
import { QRCodeSVG } from "qrcode.react";
import { platformAuth } from "@/lib/adminApi";
import { ADMIN_BASE } from "./AdminApp";

type Phase = "credentials" | "totp-setup" | "totp-verify";

export function AdminLogin() {
  const [phase, setPhase] = useState<Phase>("credentials");
  const [tempToken, setTempToken] = useState("");
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [code, setCode] = useState("");
  const [totpSetup, setTotpSetup] = useState<{ secret: string; otpAuthUri: string } | null>(null);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);

  async function handleLogin(e: React.FormEvent) {
    e.preventDefault();
    setError("");
    setLoading(true);
    try {
      const res = await platformAuth.login(username, password);
      setTempToken(res.tempToken);

      if (res.totpSetupRequired) {
        const setup = await platformAuth.totpSetup(res.tempToken);
        setTotpSetup(setup);
        setPhase("totp-setup");
      } else if (res.totpEnabled) {
        setPhase("totp-verify");
      } else {
        // No TOTP — shouldn't happen after first login
        localStorage.setItem("adminToken", res.tempToken);
        window.location.href = ADMIN_BASE;
      }
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : "Fehler");
    } finally {
      setLoading(false);
    }
  }

  async function handleTotpConfirm(e: React.FormEvent) {
    e.preventDefault();
    setError("");
    setLoading(true);
    try {
      const res = await platformAuth.totpConfirm(tempToken, code);
      localStorage.setItem("adminToken", res.token);
      window.location.href = ADMIN_BASE;
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : "Fehler");
    } finally {
      setLoading(false);
    }
  }

  async function handleTotpVerify(e: React.FormEvent) {
    e.preventDefault();
    setError("");
    setLoading(true);
    try {
      const res = await platformAuth.totpVerify(tempToken, code);
      localStorage.setItem("adminToken", res.token);
      window.location.href = ADMIN_BASE;
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : "Fehler");
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="min-h-screen bg-zinc-950 flex items-center justify-center p-4">
      <div className="w-full max-w-sm">
        <div className="text-center mb-8">
          <p className="text-xs uppercase tracking-widest text-zinc-500 mb-1">Platform</p>
          <h1 className="text-xl font-semibold text-zinc-100">HITSight Admin</h1>
        </div>

        <div className="bg-zinc-900 border border-zinc-800 rounded-xl p-6 space-y-4">
          {phase === "credentials" && (
            <form onSubmit={handleLogin} className="space-y-4">
              <div className="space-y-1">
                <label className="text-xs text-zinc-400">Benutzername</label>
                <input
                  className="w-full bg-zinc-800 border border-zinc-700 rounded-lg px-3 py-2 text-sm text-zinc-100 outline-none focus:border-zinc-500"
                  value={username}
                  onChange={e => setUsername(e.target.value)}
                  autoFocus
                  required
                />
              </div>
              <div className="space-y-1">
                <label className="text-xs text-zinc-400">Passwort</label>
                <input
                  type="password"
                  className="w-full bg-zinc-800 border border-zinc-700 rounded-lg px-3 py-2 text-sm text-zinc-100 outline-none focus:border-zinc-500"
                  value={password}
                  onChange={e => setPassword(e.target.value)}
                  required
                />
              </div>
              {error && <p className="text-xs text-red-400">{error}</p>}
              <button
                type="submit"
                disabled={loading}
                className="w-full bg-zinc-100 text-zinc-900 rounded-lg py-2 text-sm font-semibold hover:bg-white disabled:opacity-50"
              >
                {loading ? "..." : "Anmelden"}
              </button>
            </form>
          )}

          {phase === "totp-setup" && totpSetup && (
            <form onSubmit={handleTotpConfirm} className="space-y-4">
              <p className="text-sm text-zinc-300 font-medium">2FA einrichten</p>
              <p className="text-xs text-zinc-500">
                Scanne diesen QR-Code mit deiner Authenticator-App.
              </p>
              <div className="flex justify-center py-2">
                <div className="bg-white p-3 rounded-lg">
                  <QRCodeSVG value={totpSetup.otpAuthUri} size={180} />
                </div>
              </div>
              <details className="text-xs text-zinc-500">
                <summary className="cursor-pointer hover:text-zinc-300">Manuell eingeben</summary>
                <code className="block mt-2 bg-zinc-800 px-3 py-2 rounded text-zinc-300 break-all select-all">
                  {totpSetup.secret}
                </code>
              </details>
              <div className="space-y-1">
                <label className="text-xs text-zinc-400">Code bestätigen</label>
                <input
                  className="w-full bg-zinc-800 border border-zinc-700 rounded-lg px-3 py-2 text-sm text-zinc-100 outline-none focus:border-zinc-500 tracking-widest"
                  value={code}
                  onChange={e => setCode(e.target.value.replace(/\D/g, "").slice(0, 6))}
                  placeholder="000000"
                  autoFocus
                  required
                />
              </div>
              {error && <p className="text-xs text-red-400">{error}</p>}
              <button
                type="submit"
                disabled={loading || code.length !== 6}
                className="w-full bg-zinc-100 text-zinc-900 rounded-lg py-2 text-sm font-semibold hover:bg-white disabled:opacity-50"
              >
                {loading ? "..." : "Bestätigen & einloggen"}
              </button>
            </form>
          )}

          {phase === "totp-verify" && (
            <form onSubmit={handleTotpVerify} className="space-y-4">
              <p className="text-sm text-zinc-300 font-medium">2FA-Code eingeben</p>
              <input
                className="w-full bg-zinc-800 border border-zinc-700 rounded-lg px-3 py-2 text-sm text-zinc-100 outline-none focus:border-zinc-500 tracking-widest text-center text-lg"
                value={code}
                onChange={e => setCode(e.target.value.replace(/\D/g, "").slice(0, 6))}
                placeholder="000000"
                autoFocus
                required
              />
              {error && <p className="text-xs text-red-400">{error}</p>}
              <button
                type="submit"
                disabled={loading || code.length !== 6}
                className="w-full bg-zinc-100 text-zinc-900 rounded-lg py-2 text-sm font-semibold hover:bg-white disabled:opacity-50"
              >
                {loading ? "..." : "Verifizieren"}
              </button>
              <button
                type="button"
                onClick={() => { setPhase("credentials"); setCode(""); setError(""); }}
                className="w-full text-zinc-500 text-xs hover:text-zinc-300"
              >
                Zurück
              </button>
            </form>
          )}
        </div>
      </div>
    </div>
  );
}
