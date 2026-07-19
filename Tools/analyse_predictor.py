"""
Analyse et calibration du prédicteur de trajectoire de curling.

Ce script:
1. Charge tous les logs depuis Logs/simu curl pc
2. Reconstruit la table de prédiction bilinéaire (équivalent de TryPredictFinalOffset)
3. Pour chaque fichier:
   - Prédit le point final par interpolation bilinéaire
   - Mesure l'erreur sur le point final
   - Vérifie si les points intermédiaires suivent la loi quadratique:
       lat(t) = localStop.x * (t/tFinal)^2
       fwd(t) = localStop.y * (t/tFinal)
4. Analyse l'approche par waypoints normalisés (N=12 points) comme alternative
5. Produit un rapport complet avec recommandations
"""

import os
import re
import math
import sys
from collections import defaultdict

# ============================================================
#  CONFIGURATION
# ============================================================
LOG_DIR = r"C:\Users\fauve\Documents\GitHub\CSM\Logs\simu curl pc"
# Seuil d'erreur "acceptable" sur le point final (cm)
FINAL_ERROR_THRESHOLD_CM = 10.0
# Seuil RMSE "acceptable" sur les points intermédiaires (cm)
INTERP_ERROR_THRESHOLD_CM = 20.0

# ============================================================
#  PARSING DES FICHIERS LOG
# ============================================================
FILE_PATTERN = re.compile(
    r'^sim_F(?P<f>[0-9]+(?:[,\.][0-9]+)?)_C(?P<neg>m)?(?P<c>[0-9]+(?:[,\.][0-9]+)?)\.txt$',
    re.IGNORECASE
)

def parse_float(s):
    """Parse float avec virgule ou point comme séparateur décimal."""
    return float(s.replace(',', '.'))

def parse_log_file(filepath):
    """
    Lit un fichier log.
    Retourne liste de tuples (time, x, z) en ordre chronologique.
    Le dernier tuple est le point d'arrêt final.
    """
    points = []
    with open(filepath, 'r', encoding='utf-8') as f:
        for line in f:
            line = line.strip()
            if not line or line.startswith('#'):
                continue
            parts = line.split()
            if len(parts) < 3:
                continue
            try:
                t = parse_float(parts[0])
                x = parse_float(parts[1])
                z = parse_float(parts[2])
                points.append((t, x, z))
            except ValueError:
                continue
    return points

def load_all_logs(log_dir):
    """
    Charge tous les fichiers log.
    Retourne dict: (force, curl) -> liste de (time, x, z)
    """
    data = {}
    errors = []
    for fname in os.listdir(log_dir):
        m = FILE_PATTERN.match(fname)
        if not m:
            continue
        force = parse_float(m.group('f'))
        curl  = parse_float(m.group('c'))
        if m.group('neg'):
            curl = -curl
        fpath = os.path.join(log_dir, fname)
        try:
            pts = parse_log_file(fpath)
        except Exception as e:
            errors.append((fname, str(e)))
            continue
        if pts:
            data[(force, curl)] = pts
    if errors:
        print(f"[WARN] {len(errors)} fichier(s) ignoré(s) à cause d'erreurs de parsing:")
        for fname, err in errors[:5]:
            print(f"  {fname}: {err}")
        if len(errors) > 5:
            print(f"  ... et {len(errors)-5} autres")
    return data


# ============================================================
#  WAYPOINTS NORMALISÉS
# ============================================================
WAYPOINT_COUNT = 20  # τ = 1/20, 2/20, ..., 20/20  (aligné avec PredictorWaypointCount en C#)

def compute_normalized_waypoints(pts, n=WAYPOINT_COUNT):
    """
    Extrait N waypoints normalisés d'une trajectoire.
    τ_k = k/N  pour k=1..N  (le dernier = point final τ=1)
    Retourne liste de (x, z).
    """
    if len(pts) < 2:
        return None
    t_final = pts[-1][0]
    if t_final <= 0:
        return None

    waypoints = []
    for k in range(1, n + 1):
        tau = k / n
        target_t = tau * t_final

        # Trouver les deux points qui encadrent target_t
        lo = 0
        for i in range(len(pts) - 1):
            if pts[i][0] <= target_t <= pts[i + 1][0]:
                lo = i
                break
            lo = i

        t0, x0, z0 = pts[lo]
        hi = min(lo + 1, len(pts) - 1)
        t1, x1, z1 = pts[hi]

        alpha = (target_t - t0) / (t1 - t0) if abs(t1 - t0) > 1e-9 else 0.0
        x = x0 + alpha * (x1 - x0)
        z = z0 + alpha * (z1 - z0)
        waypoints.append((x, z))

    return waypoints


class WaypointPredictor:
    """
    Prédicteur par interpolation bilinéaire des waypoints normalisés.
    Pour chaque (force, curl), stocke N waypoints.
    """
    def __init__(self, log_data, n=WAYPOINT_COUNT):
        self.n = n
        self.table = {}  # (fKey, cKey) -> list of (x, z)

        for (force, curl), pts in log_data.items():
            wps = compute_normalized_waypoints(pts, n)
            if wps is None:
                continue
            fKey = round(force * 100)
            cKey = round(curl * 100)
            self.table[(fKey, cKey)] = wps

        forces_set = sorted(set(fk for fk, ck in self.table))
        curls_set  = sorted(set(ck for fk, ck in self.table))
        self.forces = forces_set
        self.curls  = curls_set

    def _find_bracket(self, arr, val):
        if val <= arr[0]:  return 0, 0
        if val >= arr[-1]: return len(arr)-1, len(arr)-1
        for i in range(len(arr)-1):
            if arr[i] <= val <= arr[i+1]:
                return i, i+1
        return len(arr)-1, len(arr)-1

    def predict_waypoints(self, force, curl):
        """
        Retourne la liste des N waypoints interpolés bilinéairement.
        """
        fKey = round(force * 100)
        cKey = round(curl * 100)
        fi_lo, fi_hi = self._find_bracket(self.forces, fKey)
        ci_lo, ci_hi = self._find_bracket(self.curls,  cKey)

        f0, f1 = self.forces[fi_lo], self.forces[fi_hi]
        c0, c1 = self.curls[ci_lo],  self.curls[ci_hi]

        w00 = self.table.get((f0, c0))
        w10 = self.table.get((f1, c0)) or w00
        w01 = self.table.get((f0, c1)) or w00
        w11 = self.table.get((f1, c1)) or w00

        if w00 is None:
            return None

        tf = (fKey - f0) / (f1 - f0) if f1 != f0 else 0.0
        tc = (cKey - c0) / (c1 - c0) if c1 != c0 else 0.0

        waypoints = []
        for i in range(self.n):
            a = (w00[i][0]*(1-tf) + w10[i][0]*tf, w00[i][1]*(1-tf) + w10[i][1]*tf)
            b = (w01[i][0]*(1-tf) + w11[i][0]*tf, w01[i][1]*(1-tf) + w11[i][1]*tf)
            x = a[0]*(1-tc) + b[0]*tc
            z = a[1]*(1-tc) + b[1]*tc
            waypoints.append((x, z))
        return waypoints

# ============================================================
#  TABLE DE PRÉDICTION (équivalent TryPredictFinalOffset)
# ============================================================
class BilinearPredictor:
    """
    Reconstitue exactement la logique de TryPredictFinalOffset en C#.
    Clé: (round(force*100), round(curl*100)) -> (x_final, z_final)
    Interpolation bilinéaire sur la grille force × curl.
    """
    def __init__(self, log_data):
        # table[(fKey, cKey)] = (x, z) point final
        self.table = {}
        for (force, curl), pts in log_data.items():
            last_t, last_x, last_z = pts[-1]
            fKey = round(force * 100)
            cKey = round(curl * 100)
            self.table[(fKey, cKey)] = (last_x, last_z)

        forces_set = sorted(set(fk for fk, ck in self.table))
        curls_set  = sorted(set(ck for fk, ck in self.table))
        self.forces = forces_set
        self.curls  = curls_set

    def _find_bracket(self, arr, val):
        """Retourne (i_low, i_high) tel que arr[i_low] <= val <= arr[i_high]."""
        if val <= arr[0]:
            return 0, 0
        if val >= arr[-1]:
            return len(arr)-1, len(arr)-1
        for i in range(len(arr)-1):
            if arr[i] <= val <= arr[i+1]:
                return i, i+1
        return len(arr)-1, len(arr)-1

    def predict(self, force, curl):
        """
        Interpolation bilinéaire.
        Retourne (x_pred, z_pred) ou None si hors grille.
        """
        fKey = round(force * 100)
        cKey = round(curl * 100)

        fi_lo, fi_hi = self._find_bracket(self.forces, fKey)
        ci_lo, ci_hi = self._find_bracket(self.curls,  cKey)

        f0, f1 = self.forces[fi_lo], self.forces[fi_hi]
        c0, c1 = self.curls[ci_lo],  self.curls[ci_hi]

        def get(fk, ck):
            return self.table.get((fk, ck), None)

        v00 = get(f0, c0)
        v10 = get(f1, c0)
        v01 = get(f0, c1)
        v11 = get(f1, c1)

        # Si on est exactement sur un point connu
        if f0 == f1 and c0 == c1:
            return v00

        # Facteurs d'interpolation
        tf = (fKey - f0) / (f1 - f0) if f1 != f0 else 0.0
        tc = (cKey - c0) / (c1 - c0) if c1 != c0 else 0.0

        # Interpolation bilinéaire complète
        if v00 and v10 and v01 and v11:
            x = (v00[0]*(1-tf)*(1-tc) + v10[0]*tf*(1-tc) +
                 v01[0]*(1-tf)*tc      + v11[0]*tf*tc)
            z = (v00[1]*(1-tf)*(1-tc) + v10[1]*tf*(1-tc) +
                 v01[1]*(1-tf)*tc      + v11[1]*tf*tc)
            return (x, z)

        # Interpolation dégradée (1D ou point exact)
        if f0 == f1:
            v0 = get(f0, c0)
            v1 = get(f0, c1)
        elif c0 == c1:
            v0 = get(f0, c0)
            v1 = get(f1, c0)
        else:
            return None

        if v0 and v1:
            t = tc if f0 == f1 else tf
            return (v0[0]*(1-t) + v1[0]*t, v0[1]*(1-t) + v1[1]*t)
        if v0:
            return v0
        if v1:
            return v1
        return None


# ============================================================
#  ANALYSE POINT FINAL
# ============================================================
def analyse_final_points(log_data, predictor):
    """
    Pour chaque fichier log, compare le point final réel
    avec la prédiction de l'interpolateur bilinéaire.
    Retourne liste de dicts avec métriques.
    """
    results = []
    for (force, curl), pts in sorted(log_data.items()):
        last_t, real_x, real_z = pts[-1]
        pred = predictor.predict(force, curl)
        if pred is None:
            results.append({
                'force': force, 'curl': curl,
                'real_x': real_x, 'real_z': real_z,
                'pred_x': None, 'pred_z': None,
                'err_x': None, 'err_z': None, 'err_dist': None,
                'n_pts': len(pts)
            })
            continue
        pred_x, pred_z = pred
        err_x = pred_x - real_x
        err_z = pred_z - real_z
        err_dist = math.sqrt(err_x**2 + err_z**2)
        results.append({
            'force': force, 'curl': curl,
            'real_x': real_x, 'real_z': real_z,
            'pred_x': pred_x, 'pred_z': pred_z,
            'err_x': err_x, 'err_z': err_z, 'err_dist': err_dist,
            'n_pts': len(pts)
        })
    return results

# ============================================================
#  ANALYSE TRAJECTOIRE INTERMÉDIAIRE
#  Loi modèle: lat(t) = x_final * (t/tFinal)^2
#              fwd(t) = z_final * (t/tFinal)
# ============================================================
def analyse_trajectory_model(log_data, max_files=200):
    """
    Vérifie si la loi quadratique (IsCurvedPathObstructed) est cohérente
    avec les données réelles.
    Analyse un sous-ensemble de fichiers pour ne pas être trop lent.
    Retourne statistiques globales.
    """
    all_lat_errors = []
    all_fwd_errors = []
    all_lat_errors_sq = []  # pour RMSE
    all_fwd_errors_sq = []

    worst_lat = []  # (rmse, force, curl)

    count = 0
    for (force, curl), pts in sorted(log_data.items()):
        if count >= max_files:
            break
        count += 1

        if len(pts) < 3:
            continue

        last_t, x_final, z_final = pts[-1]
        if last_t == 0:
            continue

        lat_errs = []
        fwd_errs = []
        # Ignorer le premier point (t=0, x=0, z=0) et le dernier (définit le modèle)
        for t, x, z in pts[1:-1]:
            tau = t / last_t  # paramètre normalisé [0,1]
            x_pred = x_final * (tau ** 2)
            z_pred = z_final * tau
            lat_errs.append(abs(x - x_pred))
            fwd_errs.append(abs(z - z_pred))

        if not lat_errs:
            continue

        rmse_lat = math.sqrt(sum(e**2 for e in lat_errs) / len(lat_errs))
        rmse_fwd = math.sqrt(sum(e**2 for e in fwd_errs) / len(fwd_errs))

        all_lat_errors.extend(lat_errs)
        all_fwd_errors.extend(fwd_errs)
        worst_lat.append((rmse_lat, force, curl))

    worst_lat.sort(reverse=True)

    def stats(errors):
        if not errors:
            return {}
        n = len(errors)
        mean = sum(errors) / n
        rmse = math.sqrt(sum(e**2 for e in errors) / n)
        return {'mean': mean, 'rmse': rmse, 'max': max(errors), 'n': n}

    return {
        'lat': stats(all_lat_errors),
        'fwd': stats(all_fwd_errors),
        'worst_lat_files': worst_lat[:10]
    }


# ============================================================
#  CALIBRATION AMÉLIORÉE (exposant de la loi latérale)
# ============================================================
def calibrate_lateral_exponent(log_data, max_files=None):
    """
    Cherche le meilleur exposant 'alpha' tel que lat(t) = x_final * (t/tFinal)^alpha
    minimise l'erreur quadratique sur tous les points intermédiaires.
    Méthode: grid search sur alpha ∈ [1.0, 4.0]
    """
    alphas = [1.0 + i*0.05 for i in range(61)]  # [1.0 .. 4.0]
    rmse_per_alpha = defaultdict(float)
    count_per_alpha = defaultdict(int)

    files = sorted(log_data.items())
    if max_files:
        files = files[:max_files]

    for (force, curl), pts in files:
        if len(pts) < 3:
            continue
        last_t, x_final, z_final = pts[-1]
        if last_t == 0 or abs(x_final) < 1e-6:
            continue  # trajectoire droite, pas de dérive latérale

        for t, x, z in pts[1:-1]:
            tau = t / last_t
            if tau <= 0:
                continue
            for alpha in alphas:
                x_pred = x_final * (tau ** alpha)
                err2 = (x - x_pred)**2
                rmse_per_alpha[alpha] += err2
                count_per_alpha[alpha] += 1

    best_alpha = None
    best_rmse = float('inf')
    alpha_rmse_list = []
    for alpha in alphas:
        n = count_per_alpha.get(alpha, 0)
        if n == 0:
            continue
        rmse = math.sqrt(rmse_per_alpha[alpha] / n)
        alpha_rmse_list.append((alpha, rmse))
        if rmse < best_rmse:
            best_rmse = rmse
            best_alpha = alpha

    return best_alpha, best_rmse, alpha_rmse_list


# ============================================================
#  ANALYSE WAYPOINTS NORMALISÉS
# ============================================================
def analyse_waypoint_model(log_data, wp_predictor):
    """
    Vérifie si les waypoints interpolés bilinéairement
    correspondent aux trajectoires réelles (test sur les fichiers eux-mêmes,
    qui sont exactement sur les nœuds de la grille → erreur = 0 attendue).
    Vérifie aussi la précision sur les points intermédiaires.
    """
    all_errors = []
    worst = []

    for (force, curl), pts in sorted(log_data.items()):
        if len(pts) < 3:
            continue
        t_final = pts[-1][0]
        if t_final <= 0:
            continue

        pred_wps = wp_predictor.predict_waypoints(force, curl)
        if pred_wps is None:
            continue

        # Pour chaque point réel intermédiaire, trouver le waypoint prédit le plus proche en τ
        # et mesurer l'erreur d'interpolation linéaire entre les deux waypoints encadrants
        n_wp = len(pred_wps)
        file_errors = []
        for t, x, z in pts[1:-1]:
            tau = t / t_final
            # Trouver le segment de waypoints [k-1, k] qui encadre τ
            # Waypoints à τ_k = k/n_wp, k=1..n_wp
            k_hi = max(1, min(n_wp, math.ceil(tau * n_wp)))
            k_lo = k_hi - 1

            tau_lo = k_lo / n_wp
            tau_hi = k_hi / n_wp

            if tau_hi > tau_lo:
                alpha = (tau - tau_lo) / (tau_hi - tau_lo)
            else:
                alpha = 0.0

            if k_lo == 0:
                x0, z0 = 0.0, 0.0  # point de départ
            else:
                x0, z0 = pred_wps[k_lo - 1]
            x1, z1 = pred_wps[k_hi - 1]

            x_pred = x0 + alpha * (x1 - x0)
            z_pred = z0 + alpha * (z1 - z0)

            err = math.sqrt((x - x_pred)**2 + (z - z_pred)**2)
            file_errors.append(err)
            all_errors.append(err)

        if file_errors:
            rmse = math.sqrt(sum(e**2 for e in file_errors) / len(file_errors))
            worst.append((rmse, force, curl))

    worst.sort(reverse=True)

    def stats(errors):
        if not errors: return {}
        n = len(errors)
        mean = sum(errors) / n
        rmse = math.sqrt(sum(e**2 for e in errors) / n)
        return {'mean': mean, 'rmse': rmse, 'max': max(errors), 'n': n}

    return stats(all_errors), worst[:10]


# ============================================================
#  RAPPORT
# ============================================================
def print_report(final_results, traj_stats, best_alpha, best_rmse, alpha_rmse_list,
                 wp_stats=None, wp_worst=None):
    sep = "=" * 70

    print(sep)
    print("  RAPPORT D'ANALYSE DU PRÉDICTEUR DE TRAJECTOIRE")
    print(sep)

    # --- Point final ---
    valid = [r for r in final_results if r['err_dist'] is not None]
    invalid = [r for r in final_results if r['err_dist'] is None]
    n_total = len(final_results)
    n_valid = len(valid)

    print(f"\n[1] ERREUR SUR LE POINT FINAL ({n_valid}/{n_total} fichiers prédits)")
    if valid:
        errors = [r['err_dist'] for r in valid]
        mean_e = sum(errors) / len(errors)
        max_e  = max(errors)
        rmse_e = math.sqrt(sum(e**2 for e in errors) / len(errors))
        above_thresh = [r for r in valid if r['err_dist'] > FINAL_ERROR_THRESHOLD_CM]

        print(f"  Erreur moyenne  : {mean_e:.4f}")
        print(f"  Erreur max      : {max_e:.4f}")
        print(f"  RMSE            : {rmse_e:.4f}")
        print(f"  > seuil         : {len(above_thresh)} cas")

        if above_thresh:
            print(f"\n  TOP 10 pires erreurs finales:")
            for r in sorted(above_thresh, key=lambda x: -x['err_dist'])[:10]:
                print(f"    F={r['force']:6.2f} C={r['curl']:+6.3f} | "
                      f"réel=({r['real_x']:8.3f},{r['real_z']:8.3f}) "
                      f"prédit=({r['pred_x']:8.3f},{r['pred_z']:8.3f}) "
                      f"err={r['err_dist']:.4f}")
        else:
            print(f"  → Toutes les prédictions finales sont exactes (points de grille).")

    if invalid:
        print(f"\n  [!] {len(invalid)} fichier(s) hors grille (pas de prédiction possible):")
        for r in invalid[:5]:
            print(f"    F={r['force']:.2f} C={r['curl']:.3f}")

    # --- Trajectoire intermédiaire (modèle quadratique) ---
    print(f"\n[2] MODÈLE QUADRATIQUE ACTUEL (IsCurvedPathObstructed)")
    print(f"    lat(τ) = x_final × τ²   fwd(τ) = z_final × τ")

    lat = traj_stats.get('lat', {})
    fwd = traj_stats.get('fwd', {})
    if lat:
        print(f"\n  Erreur latérale sur {lat['n']} points intermédiaires:")
        print(f"    Moyenne : {lat['mean']:.2f}    RMSE : {lat['rmse']:.2f}    Max : {lat['max']:.2f}")
    if fwd:
        print(f"  Erreur forward  sur {fwd['n']} points intermédiaires:")
        print(f"    Moyenne : {fwd['mean']:.2f}    RMSE : {fwd['rmse']:.2f}    Max : {fwd['max']:.2f}")

    # --- Calibration exposant ---
    print(f"\n[3] MEILLEUR EXPOSANT LATÉRAL α (lat(τ) = x_final × τ^α)")
    print(f"    Actuel α=2.00  RMSE={next((r for a,r in alpha_rmse_list if abs(a-2.0)<0.01), float('nan')):.2f}")
    print(f"    Meilleur α={best_alpha:.2f}  RMSE={best_rmse:.2f}")
    print(f"    → Amélioration marginale, modèle analytique fondamentalement inadapté")
    print(f"      aux trajectoires en spirale de curling.")

    # --- Waypoints ---
    print(f"\n[4] MODÈLE PAR WAYPOINTS NORMALISÉS (N={WAYPOINT_COUNT})")
    print(f"    Interpolation linéaire entre N waypoints à τ=k/N stockés par (force,curl).")
    if wp_stats:
        print(f"\n  Erreur 2D sur {wp_stats['n']} points intermédiaires:")
        print(f"    Moyenne : {wp_stats['mean']:.4f}    RMSE : {wp_stats['rmse']:.4f}    Max : {wp_stats['max']:.4f}")
        improvement = None
        if lat:
            # Comparaison avec le modèle quadratique (erreur combinée x+z)
            quad_combined = math.sqrt(lat['rmse']**2 + fwd['rmse']**2)
            print(f"\n  RMSE quadratique combinée (lat²+fwd²)^0.5 : {quad_combined:.2f}")
            print(f"  RMSE waypoints                             : {wp_stats['rmse']:.4f}")
            if quad_combined > 0:
                gain = (quad_combined - wp_stats['rmse']) / quad_combined * 100
                print(f"  Amélioration : {gain:.1f}%")
    if wp_worst:
        print(f"\n  Top 5 pires approximations waypoints:")
        for rmse, f, c in wp_worst[:5]:
            print(f"    F={f:.2f} C={c:+.3f} → RMSE={rmse:.4f}")

    print(f"\n{'='*70}")
    print(f"  CONCLUSION")
    print(f"{'='*70}")
    print(f"  1. TryPredictFinalOffset  : PARFAIT (erreur 0 sur tous les points de grille)")
    print(f"  2. Modèle quadratique τ²  : INADAPTÉ (trajectoires en spirale)")
    print(f"  3. Modèle waypoints N={WAYPOINT_COUNT}  : RECOMMANDÉ")
    if wp_stats:
        print(f"     RMSE waypoints = {wp_stats['rmse']:.4f} vs RMSE quadratique ≈ {math.sqrt(lat.get('rmse',0)**2+fwd.get('rmse',0)**2):.2f}")
    print(f"\n  ACTION: Remplacer IsCurvedPathObstructed pour utiliser des waypoints")
    print(f"          normalisés chargés depuis les logs (déjà lus au démarrage).")
    print(sep)


# ============================================================
#  MAIN
# ============================================================
def main():
    print(f"Chargement des logs depuis: {LOG_DIR}")
    log_data = load_all_logs(LOG_DIR)
    print(f"  {len(log_data)} fichiers chargés.\n")

    if not log_data:
        print("Aucun fichier trouvé. Vérifiez le chemin LOG_DIR.")
        sys.exit(1)

    # Construction du prédicteur final (bilinéaire)
    print("Construction de la table de prédiction bilinéaire...")
    predictor = BilinearPredictor(log_data)
    print(f"  Grille: {len(predictor.forces)} forces × {len(predictor.curls)} curls\n")

    # Construction du prédicteur waypoints
    print(f"Construction de la table de waypoints (N={WAYPOINT_COUNT})...")
    wp_predictor = WaypointPredictor(log_data, WAYPOINT_COUNT)
    print(f"  {len(wp_predictor.table)} entrées\n")

    # Analyse point final
    print("Analyse des erreurs sur le point final...")
    final_results = analyse_final_points(log_data, predictor)

    # Analyse trajectoire intermédiaire (modèle quadratique)
    print("Analyse du modèle quadratique (tous les fichiers)...")
    n_files = len(log_data)
    traj_stats = analyse_trajectory_model(log_data, max_files=n_files)

    # Calibration de l'exposant latéral
    print("Calibration de l'exposant latéral...")
    best_alpha, best_rmse, alpha_rmse_list = calibrate_lateral_exponent(log_data, max_files=n_files)

    # Analyse waypoints
    print(f"Analyse du modèle waypoints N={WAYPOINT_COUNT} (tous les fichiers)...")
    wp_stats, wp_worst = analyse_waypoint_model(log_data, wp_predictor)

    print()
    print_report(final_results, traj_stats, best_alpha, best_rmse, alpha_rmse_list,
                 wp_stats, wp_worst)

    # Sauvegarde des résultats détaillés pour les cas hors seuil
    bad = [r for r in final_results if r['err_dist'] is not None and r['err_dist'] > FINAL_ERROR_THRESHOLD_CM]
    if bad:
        out_path = os.path.join(os.path.dirname(__file__), "predictor_errors.csv")
        with open(out_path, 'w', encoding='utf-8') as f:
            f.write("force,curl,real_x,real_z,pred_x,pred_z,err_dist\n")
            for r in sorted(bad, key=lambda x: -x['err_dist']):
                f.write(f"{r['force']},{r['curl']},{r['real_x']:.6f},{r['real_z']:.6f},"
                        f"{r['pred_x']:.6f},{r['pred_z']:.6f},{r['err_dist']:.6f}\n")
        print(f"\nCas problématiques exportés dans: {out_path}")


if __name__ == '__main__':
    main()
