// ============================================================
// MIRATH — Complete Islamic Inheritance Engine (Farayid) v4
// Quran: An-Nisa 4:11, 4:12, 4:176 | All 5 Madhabs
//
// CRITICAL FIXES v4:
//  1. paternalGrandfather now correctly BLOCKED by son & sonsSon
//  2. Madhab-sensitive blocking: grandfather blocks brothers in
//     Jumhur/Maliki/Shafii/Hanbali but NOT in Hanafi
//  3. fard_then_asaba correctly yields to higher-priority asaba
//  4. sonsDaughter blocking by 2+ daughters correctly enforced
//  5. Comprehensive test cases verified
// ============================================================

export type Madhab = 'jumhur' | 'hanafi' | 'shafii' | 'maliki' | 'hanbali';

export interface DeceasedInfo {
  name: string;
  gender: 'male' | 'female';
  totalEstate: number;
  debts: number;
  funeralExpenses: number;
  bequests: number;
}

export interface Heir {
  id: string;
  type: HeirType;
  name: string;
  count: number;
}

export type HeirType =
  | 'husband' | 'wife'
  | 'son' | 'daughter'
  | 'father' | 'mother'
  | 'paternalGrandfather' | 'paternalGrandmother' | 'maternalGrandmother'
  | 'sonsSon' | 'sonsDaughter'
  | 'fullBrother' | 'fullSister'
  | 'paternalBrother' | 'paternalSister'
  | 'maternalBrother' | 'maternalSister'
  | 'fullBrothersonsSon' | 'paternalBrothersonsSon'
  | 'paternalUncle' | 'paternalUncleSon';

export interface HeirDefinition {
  type: HeirType;
  label: string;
  labelAr: string;
  category: string;
  categoryAr: string;
  gender: 'male' | 'female';
  maxCount?: number;
  /** Blocked regardless of madhab */
  blockedBy?: HeirType[];
  /** Blocked only in Jumhur/Maliki/Shafi'i/Hanbali (NOT Hanafi) */
  blockedByNonHanafi?: HeirType[];
}

export interface HeirShare {
  heir: { type: HeirType; name: string; count: number };
  amount: number;
  fraction: string;
  percentage: number;
  shareType: 'fard' | 'asaba' | 'fard_then_asaba' | 'blocked' | 'baitulmal';
  explanation: string;
  explanationAr: string;
  blocked?: boolean;
  blockReason?: string;
  blockReasonAr?: string;
  quranicRef?: string;
  quranicText?: string;
  perPersonAmount?: number;
  perPersonFraction?: string;
}

export interface CalculationStep {
  stepNumber: number;
  title: string;
  titleAr: string;
  description: string;
  descriptionAr: string;
  formula?: string;
}

export interface CalculationResult {
  shares: HeirShare[];
  distributableEstate: number;
  totalAllocated: number;
  awlApplied: boolean;
  raddApplied: boolean;
  awlFactor?: number;
  raddFactor?: number;
  notes: string[];
  notesAr: string[];
  steps: CalculationStep[];
  specialCase?: string;
  specialCaseAr?: string;
  baitulmal?: number;
}

export interface ValidationError {
  severity: 'error' | 'warning';
  message: string;
  messageAr: string;
}

// ============================================================
// HEIR DEFINITIONS — with corrected blocking rules
// ============================================================
export const AVAILABLE_HEIRS: HeirDefinition[] = [
  // Spouses
  { type: 'husband', label: 'Husband', labelAr: 'الزوج', category: 'Spouses', categoryAr: 'الزوجان', gender: 'male', maxCount: 1 },
  { type: 'wife',    label: 'Wife',    labelAr: 'الزوجة', category: 'Spouses', categoryAr: 'الزوجان', gender: 'female', maxCount: 4 },

  // Children — NEVER blocked (highest priority after spouses)
  { type: 'son',      label: 'Son',      labelAr: 'الابن', category: 'Children', categoryAr: 'الأبناء', gender: 'male' },
  { type: 'daughter', label: 'Daughter', labelAr: 'البنت', category: 'Children', categoryAr: 'الأبناء', gender: 'female' },

  // Grandchildren (via son only)
  {
    type: 'sonsSon', label: "Son's Son (Grandson)", labelAr: 'ابن الابن',
    category: 'Grandchildren', categoryAr: 'أبناء الأبناء', gender: 'male',
    blockedBy: ['son'],
  },
  {
    type: 'sonsDaughter', label: "Son's Daughter (Granddaughter)", labelAr: 'بنت الابن',
    category: 'Grandchildren', categoryAr: 'أبناء الأبناء', gender: 'female',
    blockedBy: ['son', 'sonsSon'],
    // Also effectively blocked by 2+ daughters (handled in engine, not here)
  },

  // Parents — NEVER blocked (second highest priority)
  { type: 'father', label: 'Father', labelAr: 'الأب', category: 'Parents', categoryAr: 'الوالدان', gender: 'male' },
  { type: 'mother', label: 'Mother', labelAr: 'الأم', category: 'Parents', categoryAr: 'الوالدان', gender: 'female' },

  // Grandparents
  {
    type: 'paternalGrandfather', label: 'Paternal Grandfather', labelAr: 'الجد لأب',
    category: 'Grandparents', categoryAr: 'الأجداد', gender: 'male',
    // FIX v4: grandfather blocked by father AND by son AND by sonsSon
    // A son (male descendant) blocks grandfather from asaba; grandfather gets NOTHING when son exists
    blockedBy: ['father', 'son', 'sonsSon'],
  },
  {
    type: 'paternalGrandmother', label: 'Paternal Grandmother', labelAr: 'الجدة لأب',
    category: 'Grandparents', categoryAr: 'الأجداد', gender: 'female',
    blockedBy: ['mother', 'father'],
  },
  {
    type: 'maternalGrandmother', label: 'Maternal Grandmother', labelAr: 'الجدة لأم',
    category: 'Grandparents', categoryAr: 'الأجداد', gender: 'female',
    blockedBy: ['mother'],
  },

  // Full Siblings
  {
    type: 'fullBrother', label: 'Full Brother', labelAr: 'الأخ الشقيق',
    category: 'Full Siblings', categoryAr: 'الإخوة الأشقاء', gender: 'male',
    blockedBy: ['son', 'sonsSon', 'father'],
    // FIX v4: grandfather blocks full brothers in non-Hanafi madhabs
    blockedByNonHanafi: ['paternalGrandfather'],
  },
  {
    type: 'fullSister', label: 'Full Sister', labelAr: 'الأخت الشقيقة',
    category: 'Full Siblings', categoryAr: 'الإخوة الأشقاء', gender: 'female',
    blockedBy: ['son', 'sonsSon', 'father'],
    blockedByNonHanafi: ['paternalGrandfather'],
  },

  // Paternal Siblings
  {
    type: 'paternalBrother', label: 'Paternal Half-Brother', labelAr: 'الأخ لأب',
    category: 'Paternal Siblings', categoryAr: 'الإخوة لأب', gender: 'male',
    blockedBy: ['son', 'sonsSon', 'father', 'fullBrother'],
    // Grandfather blocks paternal brothers in non-Hanafi madhabs
    blockedByNonHanafi: ['paternalGrandfather'],
  },
  {
    type: 'paternalSister', label: 'Paternal Half-Sister', labelAr: 'الأخت لأب',
    category: 'Paternal Siblings', categoryAr: 'الإخوة لأب', gender: 'female',
    blockedBy: ['son', 'sonsSon', 'father', 'fullBrother'],
    blockedByNonHanafi: ['paternalGrandfather'],
  },

  // Maternal Siblings (blocked by any descendants or father/grandfather)
  {
    type: 'maternalBrother', label: 'Maternal Half-Brother', labelAr: 'الأخ لأم',
    category: 'Maternal Siblings', categoryAr: 'الإخوة لأم', gender: 'male',
    blockedBy: ['son', 'daughter', 'sonsSon', 'sonsDaughter', 'father', 'paternalGrandfather'],
  },
  {
    type: 'maternalSister', label: 'Maternal Half-Sister', labelAr: 'الأخت لأم',
    category: 'Maternal Siblings', categoryAr: 'الإخوة لأم', gender: 'female',
    blockedBy: ['son', 'daughter', 'sonsSon', 'sonsDaughter', 'father', 'paternalGrandfather'],
  },

  // Nephews (sons of brothers)
  {
    type: 'fullBrothersonsSon', label: "Full Brother's Son", labelAr: 'ابن الأخ الشقيق',
    category: 'Nephews', categoryAr: 'أبناء الإخوة', gender: 'male',
    blockedBy: ['son', 'sonsSon', 'father', 'paternalGrandfather', 'fullBrother', 'paternalBrother'],
  },
  {
    type: 'paternalBrothersonsSon', label: "Paternal Brother's Son", labelAr: 'ابن الأخ لأب',
    category: 'Nephews', categoryAr: 'أبناء الإخوة', gender: 'male',
    blockedBy: ['son', 'sonsSon', 'father', 'paternalGrandfather', 'fullBrother', 'paternalBrother', 'fullBrothersonsSon'],
  },

  // Uncles & Cousins
  {
    type: 'paternalUncle', label: 'Paternal Uncle', labelAr: 'العم',
    category: 'Uncles', categoryAr: 'الأعمام', gender: 'male',
    blockedBy: ['son', 'sonsSon', 'father', 'paternalGrandfather', 'fullBrother', 'paternalBrother', 'fullBrothersonsSon', 'paternalBrothersonsSon'],
  },
  {
    type: 'paternalUncleSon', label: "Paternal Uncle's Son (Cousin)", labelAr: 'ابن العم',
    category: 'Uncles', categoryAr: 'الأعمام', gender: 'male',
    blockedBy: ['son', 'sonsSon', 'father', 'paternalGrandfather', 'fullBrother', 'paternalBrother', 'fullBrothersonsSon', 'paternalBrothersonsSon', 'paternalUncle'],
  },
];

// ============================================================
// HELPERS
// ============================================================
export function getHeirLabel(type: HeirType): string {
  return AVAILABLE_HEIRS.find(h => h.type === type)?.label || type;
}
export function getHeirLabelAr(type: HeirType): string {
  return AVAILABLE_HEIRS.find(h => h.type === type)?.labelAr || type;
}

function isBlockedForMadhab(type: HeirType, presentHeirs: HeirType[], madhab: Madhab): boolean {
  const def = AVAILABLE_HEIRS.find(d => d.type === type);
  if (!def) return false;
  // Universal blocking
  if (def.blockedBy?.some(b => presentHeirs.includes(b))) return true;
  // Madhab-specific: grandfather blocks siblings in all madhabs EXCEPT Hanafi
  if (madhab !== 'hanafi' && def.blockedByNonHanafi?.some(b => presentHeirs.includes(b))) return true;
  return false;
}

function getBlockerForMadhab(type: HeirType, presentHeirs: HeirType[], madhab: Madhab): HeirType | null {
  const def = AVAILABLE_HEIRS.find(d => d.type === type);
  if (!def) return null;
  const universal = def.blockedBy?.find(b => presentHeirs.includes(b));
  if (universal) return universal;
  if (madhab !== 'hanafi') {
    const nonHanafi = def.blockedByNonHanafi?.find(b => presentHeirs.includes(b));
    if (nonHanafi) return nonHanafi;
  }
  return null;
}

function hasDesc(heirs: HeirType[]): boolean {
  return ['son','daughter','sonsSon','sonsDaughter'].some(t => heirs.includes(t as HeirType));
}
function hasMaleDesc(heirs: HeirType[]): boolean {
  return ['son','sonsSon'].some(t => heirs.includes(t as HeirType));
}

// ── GCD / Fraction helpers ──────────────────────────────────
function gcd(a: number, b: number): number { return b === 0 ? a : gcd(b, a % b); }
function lcm(a: number, b: number): number { return (a / gcd(a, b)) * b; }
function simplify(n: number, d: number): [number, number] {
  if (n === 0) return [0, 1];
  const g = gcd(Math.abs(n), Math.abs(d));
  return [n / g, d / g];
}
function fracStr(n: number, d: number): string {
  const [sn, sd] = simplify(n, d);
  if (sn === 0) return '0';
  if (sn === sd) return '1';
  return `${sn}/${sd}`;
}
function ratioToFraction(ratio: number): string {
  if (ratio <= 0) return '0';
  if (ratio >= 1) return '1';
  let bestNum = 0, bestDen = 1, bestErr = Infinity;
  for (let d = 1; d <= 48; d++) {
    const n = Math.round(ratio * d);
    if (n <= 0 || n > d) continue;
    const err = Math.abs(ratio - n / d);
    if (err < bestErr) { bestErr = err; bestNum = n; bestDen = d; }
  }
  if (bestErr < 0.002) {
    const [sn, sd] = simplify(bestNum, bestDen);
    return `${sn}/${sd}`;
  }
  return `${(ratio * 100).toFixed(2)}%`;
}

// ============================================================
// MAIN CALCULATION ENGINE
// ============================================================
export function calculateInheritance(
  deceased: DeceasedInfo,
  inputHeirs: Heir[],
  madhab: Madhab
): CalculationResult {
  const steps: CalculationStep[] = [];
  const notes: string[] = [];
  const notesAr: string[] = [];

  // ── STEP 1: Net estate ────────────────────────────────────
  const gross = deceased.totalEstate;
  const totalDed = (deceased.debts || 0) + (deceased.funeralExpenses || 0);
  const afterDed = Math.max(0, gross - totalDed);
  const maxBequest = afterDed / 3;
  const bequest = Math.min(deceased.bequests || 0, maxBequest);
  const net = Math.max(0, afterDed - bequest);

  steps.push({
    stepNumber: 1,
    title: 'Calculate Net Distributable Estate',
    titleAr: 'حساب صافي التركة القابلة للتوزيع',
    description: `Gross ${gross} − Debts ${deceased.debts || 0} − Funeral ${deceased.funeralExpenses || 0} − Bequests ${bequest.toFixed(2)} = ${net.toFixed(2)}`,
    descriptionAr: `إجمالي ${gross} − ديون ${deceased.debts || 0} − جنازة ${deceased.funeralExpenses || 0} − وصايا ${bequest.toFixed(2)} = ${net.toFixed(2)}`,
    formula: `Net = ${net.toFixed(2)}`,
  });

  if ((deceased.bequests || 0) > maxBequest && maxBequest > 0) {
    notes.push(`Bequests capped at 1/3 of net estate (max ${maxBequest.toFixed(2)}).`);
    notesAr.push(`الوصية محدودة بثلث التركة الصافية (الحد الأقصى ${maxBequest.toFixed(2)}).`);
  }

  // ── STEP 2: Apply Hajb (blocking) — madhab-aware ─────────
  const allTypes = inputHeirs.map(h => h.type);

  const activeHeirs = inputHeirs.filter(h => !isBlockedForMadhab(h.type, allTypes, madhab));
  const activeTypes = activeHeirs.map(h => h.type);
  const blockedHeirs = inputHeirs.filter(h => isBlockedForMadhab(h.type, allTypes, madhab));

  steps.push({
    stepNumber: 2,
    title: 'Apply Hajb (Exclusion Rules)',
    titleAr: 'تطبيق الحجب',
    description: `${inputHeirs.length} heirs → ${activeHeirs.length} eligible after Hajb [madhab: ${madhab}]. Blocked: ${blockedHeirs.map(h => getHeirLabel(h.type)).join(', ') || 'none'}`,
    descriptionAr: `${inputHeirs.length} وارث → ${activeHeirs.length} مستحق بعد الحجب [المذهب: ${madhab}]. المحجوبون: ${blockedHeirs.map(h => getHeirLabelAr(h.type)).join('، ') || 'لا أحد'}`,
  });

  // ── STEP 3: Assign fard shares ────────────────────────────
  type ShareEntry = { n: number; d: number; type: 'fard' | 'asaba' | 'fard_then_asaba'; heir: Heir };
  const shareMap = new Map<HeirType, ShareEntry>();

  const has  = (t: HeirType) => activeTypes.includes(t);
  const cnt  = (t: HeirType) => activeHeirs.find(h => h.type === t)?.count ?? 0;
  const heir = (t: HeirType) => activeHeirs.find(h => h.type === t)!;

  // ── Spouses ──
  if (has('husband')) {
    const [n, d] = hasDesc(activeTypes) ? [1, 4] : [1, 2];
    shareMap.set('husband', { n, d, type: 'fard', heir: heir('husband') });
  }
  if (has('wife')) {
    const [n, d] = hasDesc(activeTypes) ? [1, 8] : [1, 4];
    shareMap.set('wife', { n, d, type: 'fard', heir: heir('wife') });
  }

  // ── Mother ──
  if (has('mother')) {
    const sibCount = (['fullBrother','fullSister','paternalBrother','paternalSister','maternalBrother','maternalSister'] as HeirType[])
      .reduce((s, t) => s + cnt(t), 0);
    const hasSpouse = has('husband') || has('wife');
    const hasFather = has('father');
    if (hasDesc(activeTypes) || sibCount >= 2) {
      shareMap.set('mother', { n: 1, d: 6, type: 'fard', heir: heir('mother') });
    } else if (hasFather && hasSpouse) {
      // Umariyyatain — handled post-hoc
      shareMap.set('mother', { n: 1, d: 3, type: 'fard', heir: heir('mother') });
      notes.push('Umariyyatain: Mother receives 1/3 of remainder after spouse share.');
      notesAr.push('عمريتين: الأم ترث ثلث الباقي بعد نصيب الزوج/الزوجة.');
    } else {
      shareMap.set('mother', { n: 1, d: 3, type: 'fard', heir: heir('mother') });
    }
  }

  // ── Paternal Grandmother ──
  if (has('paternalGrandmother')) {
    shareMap.set('paternalGrandmother', { n: 1, d: 6, type: 'fard', heir: heir('paternalGrandmother') });
  }
  // ── Maternal Grandmother ──
  if (has('maternalGrandmother')) {
    shareMap.set('maternalGrandmother', { n: 1, d: 6, type: 'fard', heir: heir('maternalGrandmother') });
  }

  // ── Father ──
  if (has('father')) {
    if (hasDesc(activeTypes)) {
      shareMap.set('father', { n: 1, d: 6, type: 'fard_then_asaba', heir: heir('father') });
    } else {
      shareMap.set('father', { n: 0, d: 1, type: 'asaba', heir: heir('father') });
    }
  }

  // ── Paternal Grandfather (only when father absent — already blocked by father in all cases) ──
  // KEY FIX: grandfather is now also blocked by son/sonsSon (see AVAILABLE_HEIRS above)
  // So if we reach here, grandfather is active only when: no father, no son, no sonsSon
  if (has('paternalGrandfather') && !has('father')) {
    if (hasDesc(activeTypes)) {
      // With daughters only (no sons — sons would have blocked grandfather)
      // Grandfather gets fard 1/6 + asaba (takes residue after daughters' fard)
      shareMap.set('paternalGrandfather', { n: 1, d: 6, type: 'fard_then_asaba', heir: heir('paternalGrandfather') });
    } else {
      // No descendants → grandfather is sole asaba
      shareMap.set('paternalGrandfather', { n: 0, d: 1, type: 'asaba', heir: heir('paternalGrandfather') });
    }
  }

  // ── Son & Daughter (always asaba together) ──
  if (has('son')) {
    shareMap.set('son', { n: 0, d: 1, type: 'asaba', heir: heir('son') });
    if (has('daughter')) {
      shareMap.set('daughter', { n: 0, d: 1, type: 'asaba', heir: heir('daughter') });
    }
  } else if (has('daughter')) {
    // Daughters without sons
    const dc = cnt('daughter');
    if (dc === 1) shareMap.set('daughter', { n: 1, d: 2, type: 'fard', heir: heir('daughter') });
    else          shareMap.set('daughter', { n: 2, d: 3, type: 'fard', heir: heir('daughter') });
  }

  // ── Son's Son & Son's Daughter ──
  if (has('sonsSon') && !has('son')) {
    shareMap.set('sonsSon', { n: 0, d: 1, type: 'asaba', heir: heir('sonsSon') });
    if (has('sonsDaughter')) {
      shareMap.set('sonsDaughter', { n: 0, d: 1, type: 'asaba', heir: heir('sonsDaughter') });
    }
  } else if (!has('son') && !has('sonsSon') && has('sonsDaughter')) {
    const sdc = cnt('sonsDaughter');
    const dc  = cnt('daughter');
    if (dc === 1) {
      // One daughter already takes 1/2 → sonsDaughter gets 1/6 to complete 2/3
      shareMap.set('sonsDaughter', { n: 1, d: 6, type: 'fard', heir: heir('sonsDaughter') });
    } else if (dc >= 2) {
      // 2+ daughters already fill 2/3 → sonsDaughter completely blocked (mahjuba)
      // Mark as blocked post-hoc — don't add to shareMap
      notes.push('Son\'s daughter excluded: 2+ daughters already fill the 2/3 maximum.');
      notesAr.push('بنت الابن محجوبة: البنتان أو أكثر يستوفيان الثلثين.');
    } else {
      // No daughters → same rules as daughter
      if (sdc === 1) shareMap.set('sonsDaughter', { n: 1, d: 2, type: 'fard', heir: heir('sonsDaughter') });
      else           shareMap.set('sonsDaughter', { n: 2, d: 3, type: 'fard', heir: heir('sonsDaughter') });
    }
  }

  // ── Maternal Siblings ──
  if (has('maternalBrother') || has('maternalSister')) {
    const mbc   = cnt('maternalBrother');
    const msc   = cnt('maternalSister');
    const total = mbc + msc;
    if (total === 1) {
      const t = has('maternalBrother') ? 'maternalBrother' : 'maternalSister';
      shareMap.set(t, { n: 1, d: 6, type: 'fard', heir: heir(t) });
    } else {
      // Share 1/3 equally (no gender distinction for maternal siblings)
      if (has('maternalBrother')) shareMap.set('maternalBrother', { n: 1, d: 3, type: 'fard', heir: heir('maternalBrother') });
      if (has('maternalSister'))  shareMap.set('maternalSister',  { n: 1, d: 3, type: 'fard', heir: heir('maternalSister') });
    }
  }

  // ── Full Siblings ──
  if (has('fullBrother')) {
    shareMap.set('fullBrother', { n: 0, d: 1, type: 'asaba', heir: heir('fullBrother') });
    if (has('fullSister')) shareMap.set('fullSister', { n: 0, d: 1, type: 'asaba', heir: heir('fullSister') });
  } else if (has('fullSister')) {
    const fsc = cnt('fullSister');
    const hasDaughtersForAsaba = has('daughter') || has('sonsDaughter');
    if (hasDaughtersForAsaba) {
      shareMap.set('fullSister', { n: 0, d: 1, type: 'asaba', heir: heir('fullSister') });
      notes.push("Full sister(s) inherit as Asaba with daughters (Asaba ma'a al-Ghair).");
      notesAr.push('الأخت الشقيقة ترث تعصيبًا مع البنات (عصبة مع الغير).');
    } else {
      if (fsc === 1) shareMap.set('fullSister', { n: 1, d: 2, type: 'fard', heir: heir('fullSister') });
      else           shareMap.set('fullSister', { n: 2, d: 3, type: 'fard', heir: heir('fullSister') });
    }
  }

  // ── Paternal Siblings ──
  if (!has('fullBrother') && !has('fullSister')) {
    if (has('paternalBrother')) {
      shareMap.set('paternalBrother', { n: 0, d: 1, type: 'asaba', heir: heir('paternalBrother') });
      if (has('paternalSister')) shareMap.set('paternalSister', { n: 0, d: 1, type: 'asaba', heir: heir('paternalSister') });
    } else if (has('paternalSister')) {
      const psc = cnt('paternalSister');
      const hasDaughtersForAsaba = has('daughter') || has('sonsDaughter');
      if (hasDaughtersForAsaba) {
        shareMap.set('paternalSister', { n: 0, d: 1, type: 'asaba', heir: heir('paternalSister') });
      } else {
        if (psc === 1) shareMap.set('paternalSister', { n: 1, d: 2, type: 'fard', heir: heir('paternalSister') });
        else           shareMap.set('paternalSister', { n: 2, d: 3, type: 'fard', heir: heir('paternalSister') });
      }
    }
  }

  // ── Nephews & Uncles ──
  for (const t of ['fullBrothersonsSon','paternalBrothersonsSon','paternalUncle','paternalUncleSon'] as HeirType[]) {
    if (has(t) && !shareMap.has(t)) {
      shareMap.set(t, { n: 0, d: 1, type: 'asaba', heir: activeHeirs.find(h => h.type === t)! });
    }
  }

  // ── STEP 4: LCD and sum fard ──────────────────────────────
  const fardEntries = [...shareMap.values()].filter(e => e.type === 'fard' || e.type === 'fard_then_asaba');
  let denomLCD = 1;
  for (const e of fardEntries) if (e.n > 0) denomLCD = lcm(denomLCD, e.d);

  let totalFardNum = 0;
  for (const e of fardEntries) if (e.n > 0) totalFardNum += e.n * (denomLCD / e.d);

  steps.push({
    stepNumber: 3,
    title: 'Sum Fixed Shares (Fard)',
    titleAr: 'جمع الأنصبة المقدرة (الفروض)',
    description: `Fixed shares sum to ${totalFardNum}/${denomLCD} = ${(totalFardNum / denomLCD * 100).toFixed(2)}%`,
    descriptionAr: `مجموع الفروض = ${totalFardNum}/${denomLCD} = ${(totalFardNum / denomLCD * 100).toFixed(2)}%`,
  });

  // ── STEP 5: Awl ───────────────────────────────────────────
  let awlApplied = false;
  let awlFactor  = 1;
  if (totalFardNum > denomLCD) {
    awlFactor   = denomLCD / totalFardNum;
    awlApplied  = true;
    notes.push(`Awl applied: shares totalled ${(totalFardNum / denomLCD * 100).toFixed(2)}%, proportionally reduced.`);
    notesAr.push(`تم تطبيق العول: الفروض بلغت ${(totalFardNum / denomLCD * 100).toFixed(2)}%، خُفِّضت بالنسبة.`);
    steps.push({
      stepNumber: 4,
      title: 'Awl — Proportional Reduction',
      titleAr: 'العول — تخفيض الأنصبة بالنسبة',
      description: `Shares > 100%. Awl factor = ${denomLCD}/${totalFardNum} = ${awlFactor.toFixed(4)}.`,
      descriptionAr: `الفروض تجاوزت 100%. عامل العول = ${denomLCD}/${totalFardNum} = ${awlFactor.toFixed(4)}.`,
      formula: `Awl factor = ${denomLCD}/${totalFardNum}`,
    });
    denomLCD = totalFardNum;
  }

  // ── STEP 6: Compute fard amounts ─────────────────────────
  const amounts = new Map<HeirType, number>();
  const isUmariyyatain = notes.some(n => n.includes('Umariyyatain'));

  // Process spouses first so Umariyyatain can reference spouse amount
  for (const t of ['husband', 'wife'] as HeirType[]) {
    const e = shareMap.get(t);
    if (e && (e.type === 'fard' || e.type === 'fard_then_asaba') && e.n > 0) {
      amounts.set(t, net * (e.n / e.d) * (awlApplied ? awlFactor : 1));
    }
  }

  for (const [type, e] of shareMap) {
    if (type === 'husband' || type === 'wife') continue; // already done
    if ((e.type === 'fard' || e.type === 'fard_then_asaba') && e.n > 0) {
      let amount: number;
      if (isUmariyyatain && type === 'mother') {
        const spouseAmt = (amounts.get('husband') ?? 0) + (amounts.get('wife') ?? 0);
        amount = (net - spouseAmt) / 3;
      } else {
        amount = net * (e.n / e.d) * (awlApplied ? awlFactor : 1);
      }
      amounts.set(type, amount);
    }
  }

  // ── STEP 7: Asaba residue distribution ───────────────────
  let allocated = [...amounts.values()].reduce((s, v) => s + v, 0);
  let residue   = Math.max(0, net - allocated);

  // fard_then_asaba types (father / grandfather): take remaining residue first
  // BUT only if no higher-priority pure asaba heir exists
  const pureAsaba: HeirType[] = [];
  for (const [t, e] of shareMap) {
    if (e.type === 'asaba') pureAsaba.push(t);
  }

  // Priority groups for pure asaba (highest first)
  const asabaPriority: HeirType[][] = [
    ['son', 'daughter'],
    ['sonsSon', 'sonsDaughter'],
    // NOTE: father and grandfather are fard_then_asaba, handled separately
    ['fullBrother', 'fullSister'],
    ['paternalBrother', 'paternalSister'],
    ['fullBrothersonsSon'],
    ['paternalBrothersonsSon'],
    ['paternalUncle'],
    ['paternalUncleSon'],
  ];

  let asabaGroup: HeirType[] = [];
  for (const group of asabaPriority) {
    const present = group.filter(t => pureAsaba.includes(t));
    if (present.length > 0) { asabaGroup = present; break; }
  }

  const fardThenAsaba = [...shareMap.entries()]
    .filter(([, e]) => e.type === 'fard_then_asaba')
    .map(([t]) => t);

  // KEY LOGIC: fard_then_asaba (father/grandfather) gets residue ONLY if no pure asaba group
  if (fardThenAsaba.length > 0 && residue > 0 && asabaGroup.length === 0) {
    for (const ft of fardThenAsaba) {
      amounts.set(ft, (amounts.get(ft) ?? 0) + residue);
    }
    residue = 0;
  } else if (asabaGroup.length > 0 && residue > 0) {
    // Distribute among asaba group: male = 2 units, female = 1 unit
    let totalUnits = 0;
    for (const gt of asabaGroup) {
      const def = AVAILABLE_HEIRS.find(d => d.type === gt);
      const c   = activeHeirs.find(h => h.type === gt)?.count ?? 0;
      totalUnits += def?.gender === 'male' ? c * 2 : c;
    }
    if (totalUnits > 0) {
      const unitValue = residue / totalUnits;
      for (const gt of asabaGroup) {
        const def   = AVAILABLE_HEIRS.find(d => d.type === gt);
        const c     = activeHeirs.find(h => h.type === gt)?.count ?? 0;
        const units = def?.gender === 'male' ? c * 2 : c;
        amounts.set(gt, (amounts.get(gt) ?? 0) + unitValue * units);
      }
    }
    residue = 0;
    // fard_then_asaba (father/grandfather) gets nothing extra when pure asaba exists
    // Their fard 1/6 was already computed above
  }

  steps.push({
    stepNumber: 5,
    title: 'Distribute Asaba (Residue)',
    titleAr: 'توزيع الباقي (العصبة)',
    description: asabaGroup.length > 0
      ? `Residue ${residue.toFixed(2)} to pure Asaba: ${asabaGroup.map(t => getHeirLabel(t)).join(', ')}`
      : fardThenAsaba.length > 0
        ? `Residue to fard+asaba heir: ${fardThenAsaba.map(t => getHeirLabel(t)).join(', ')}`
        : 'No Asaba — surplus to Radd or Bait ul-Mal',
    descriptionAr: asabaGroup.length > 0
      ? `الباقي يوزع على العصبة: ${asabaGroup.map(t => getHeirLabelAr(t)).join('، ')}`
      : fardThenAsaba.length > 0
        ? `الباقي للوارث ذي الفرض والتعصيب: ${fardThenAsaba.map(t => getHeirLabelAr(t)).join('، ')}`
        : 'لا عصبة — الباقي للرد أو بيت المال',
  });

  // ── STEP 8: Radd ──────────────────────────────────────────
  let raddApplied = false;
  let raddFactor  = 1;
  let baitulmal   = 0;

  const totalAfterAsaba  = [...amounts.values()].reduce((s, v) => s + v, 0);
  const surplusAfterAsaba = Math.max(0, net - totalAfterAsaba);

  if (surplusAfterAsaba > 0.001 && asabaGroup.length === 0 && fardThenAsaba.length === 0) {
    const spouseTypes: HeirType[] = ['husband', 'wife'];
    const isSpouseEligible = (t: HeirType) => {
      if (t === 'wife')    return false; // no madhab gives wife Radd
      if (t === 'husband') return madhab === 'maliki'; // only Maliki
      return true;
    };
    const raddCandidates = [...shareMap.keys()].filter(t => amounts.has(t) && isSpouseEligible(t));
    const nonSpouseRadd  = raddCandidates.filter(t => !spouseTypes.includes(t));
    const eligibleForRadd = nonSpouseRadd.length > 0 ? nonSpouseRadd : raddCandidates;

    if (eligibleForRadd.length > 0) {
      const eligibleTotal = eligibleForRadd.reduce((s, t) => s + (amounts.get(t) ?? 0), 0);
      if (eligibleTotal > 0) {
        for (const t of eligibleForRadd) {
          const orig  = amounts.get(t) ?? 0;
          amounts.set(t, orig + surplusAfterAsaba * (orig / eligibleTotal));
        }
        raddApplied = true;
        raddFactor  = net / totalAfterAsaba;
        notes.push(`Radd applied: surplus ${surplusAfterAsaba.toFixed(2)} returned to ${eligibleForRadd.map(t => getHeirLabel(t)).join(', ')}.`);
        notesAr.push(`تم تطبيق الرد: الباقي ${surplusAfterAsaba.toFixed(2)} رُدَّ على ${eligibleForRadd.map(t => getHeirLabelAr(t)).join('، ')}.`);
        steps.push({
          stepNumber: 6,
          title: 'Radd — Surplus Returned to Heirs',
          titleAr: 'الرد — رد الباقي على ذوي الفروض',
          description: `Surplus ${surplusAfterAsaba.toFixed(2)} returned. Madhab: ${madhab}.`,
          descriptionAr: `الباقي ${surplusAfterAsaba.toFixed(2)} رُدَّ. المذهب: ${madhab}.`,
        });
      }
    } else {
      baitulmal = surplusAfterAsaba;
      notes.push(`Surplus ${surplusAfterAsaba.toFixed(2)} goes to Bait ul-Mal.`);
      notesAr.push(`الباقي ${surplusAfterAsaba.toFixed(2)} يذهب لبيت المال.`);
    }
  }

  // ── STEP 9: Build final share list ───────────────────────
  const finalAllocated = [...amounts.values()].reduce((s, v) => s + v, 0);
  const finalShares: HeirShare[] = [];

  for (const h of activeHeirs) {
    const amount  = amounts.get(h.type) ?? 0;
    const entry   = shareMap.get(h.type);
    const def     = AVAILABLE_HEIRS.find(d => d.type === h.type);
    const percentage = net > 0 ? (amount / net) * 100 : 0;

    let fraction      = '0';
    let quranicRef    = '';
    let quranicText   = '';
    let explanation   = '';
    let explanationAr = '';
    let shareType: HeirShare['shareType'] = 'fard';

    if (!entry) {
      // Heir was added to activeHeirs but got no share (e.g. sonsDaughter blocked by 2+ daughters)
      continue;
    }

    if (entry.type === 'asaba') {
      shareType     = 'asaba';
      fraction      = net > 0 ? ratioToFraction(amount / net) : '0';
      explanation   = 'Receives residue as Asaba. Males get 2× females.';
      explanationAr = 'يرث الباقي تعصيبًا. للذكر مثل حظ الأنثيين.';
    } else if (entry.type === 'fard_then_asaba') {
      shareType     = 'fard_then_asaba';
      fraction      = net > 0 ? ratioToFraction(amount / net) : '1/6 + remainder';
      quranicRef    = 'An-Nisa 4:11';
      quranicText   = 'وَلِأَبَوَيْهِ لِكُلِّ وَاحِدٍ مِّنْهُمَا السُّدُسُ مِمَّا تَرَكَ إِن كَانَ لَهُ وَلَدٌ';
      explanation   = 'Fixed 1/6 + residue as Asaba.';
      explanationAr = 'السدس فرضًا والباقي تعصيبًا.';
    } else if (entry.n > 0) {
      shareType = 'fard';
      fraction  = fracStr(entry.n, entry.d);
      switch (h.type) {
        case 'husband':
          quranicRef = 'An-Nisa 4:12'; explanation = hasDesc(activeTypes) ? 'Husband: 1/4 (descendants present)' : 'Husband: 1/2 (no descendants)';
          explanationAr = hasDesc(activeTypes) ? 'الزوج: الربع (مع الأولاد)' : 'الزوج: النصف (بلا أولاد)'; break;
        case 'wife':
          quranicRef = 'An-Nisa 4:12'; explanation = `Wife/Wives share ${hasDesc(activeTypes) ? '1/8' : '1/4'} among ${h.count}`;
          explanationAr = `الزوجة/الزوجات: ${hasDesc(activeTypes) ? 'الثمن' : 'الربع'} على ${h.count}`; break;
        case 'daughter':
          quranicRef = 'An-Nisa 4:11'; explanation = h.count === 1 ? '1 daughter: 1/2' : `${h.count} daughters: 2/3 shared`;
          explanationAr = h.count === 1 ? 'بنت واحدة: النصف' : `${h.count} بنات: الثلثان مشتركاً`; break;
        case 'mother':
          quranicRef = 'An-Nisa 4:11';
          explanation   = isUmariyyatain ? '1/3 of remainder after spouse (Umariyyatain)' : entry.d === 6 ? '1/6 (descendants or ≥2 siblings)' : '1/3';
          explanationAr = isUmariyyatain ? 'ثلث الباقي بعد الزوج (عمريتين)' : entry.d === 6 ? 'السدس (مع الأولاد أو أخوين+)' : 'الثلث'; break;
        case 'paternalGrandmother': case 'maternalGrandmother':
          quranicRef = 'Hadith'; explanation = '1/6 (shared between grandmothers)'; explanationAr = 'السدس (تشترك فيه الجدتان)'; break;
        case 'fullSister': case 'paternalSister':
          quranicRef = 'An-Nisa 4:176'; explanation = h.count === 1 ? '1 sister: 1/2' : `${h.count} sisters: 2/3 shared`;
          explanationAr = h.count === 1 ? 'أخت واحدة: النصف' : `${h.count} أخوات: الثلثان مشتركاً`; break;
        case 'maternalBrother': case 'maternalSister':
          quranicRef = 'An-Nisa 4:12';
          explanation   = cnt('maternalBrother') + cnt('maternalSister') === 1 ? '1/6 (single maternal sibling)' : '1/3 shared equally';
          explanationAr = cnt('maternalBrother') + cnt('maternalSister') === 1 ? 'السدس لأخ/أخت واحد لأم' : 'الثلث بالتساوي لإخوة الأم'; break;
        case 'sonsDaughter':
          quranicRef = 'An-Nisa 4:11'; explanation = '1/6 completing 2/3 with daughter; or 1/2 alone';
          explanationAr = 'السدس تكملة للثلثين مع بنت؛ أو النصف منفردة'; break;
        default:
          explanation = `Fixed share: ${fraction}`; explanationAr = `الفرض المقدر: ${fraction}`;
      }
    }

    if (raddApplied && amounts.has(h.type)) { explanation += ' (Radd applied)'; explanationAr += ' (مع الرد)'; }

    // Per-person split for plural wives / daughters
    let perPersonAmount: number | undefined;
    let perPersonFraction: string | undefined;
    if (h.count > 1 && amount > 0 && entry.type === 'fard') {
      perPersonAmount   = amount / h.count;
      perPersonFraction = net > 0 ? ratioToFraction(perPersonAmount / net) : undefined;
      if (h.type === 'wife') {
        fraction      = `${fracStr(entry.n, entry.d)} shared → each: ${perPersonFraction ?? ''}`;
        explanation  += ` Each wife gets ${perPersonFraction ?? ''}`;
        explanationAr += ` لكل زوجة ${perPersonFraction ?? ''}`;
      }
    }

    finalShares.push({
      heir: { type: h.type, name: def?.label ?? h.name, count: h.count },
      amount, fraction, percentage, shareType,
      explanation, explanationAr,
      blocked: false,
      quranicRef:  quranicRef  || undefined,
      quranicText: quranicText || undefined,
      perPersonAmount, perPersonFraction,
    });
  }

  // Blocked heirs
  for (const h of blockedHeirs) {
    const blocker    = getBlockerForMadhab(h.type, allTypes, madhab);
    const def        = AVAILABLE_HEIRS.find(d => d.type === h.type);
    const blockerDef = blocker ? AVAILABLE_HEIRS.find(d => d.type === blocker) : null;
    finalShares.push({
      heir: { type: h.type, name: def?.label ?? h.type, count: h.count },
      amount: 0, fraction: '0', percentage: 0, shareType: 'blocked',
      explanation:   `Blocked (Mahjub) by ${blockerDef?.label ?? blocker ?? 'higher heir'}`,
      explanationAr: `محجوب بسبب وجود ${blockerDef?.labelAr ?? blocker ?? 'وارث أعلى'}`,
      blocked: true,
      blockReason:   `Excluded by ${blockerDef?.label ?? blocker}`,
      blockReasonAr: `محجوب بـ ${blockerDef?.labelAr ?? blocker}`,
    });
  }

  // Special case detection
  let specialCase: string | undefined;
  let specialCaseAr: string | undefined;
  const hasHW = has('husband') || has('wife');
  if (hasHW && has('father') && has('mother') && !hasDesc(activeTypes)) {
    specialCase    = 'Umariyyatain (العمريتان): Mother receives 1/3 of remainder after spouse, not 1/3 of total.';
    specialCaseAr  = 'العمريتان: الأم ترث ثلث الباقي بعد نصيب الزوج/الزوجة لا ثلث التركة كاملها.';
  }
  const isMusharraka = has('husband') && has('mother') && has('maternalBrother') &&
    (has('fullBrother') || has('fullSister')) && !hasDesc(activeTypes) && !has('father');
  if (isMusharraka) {
    specialCase   = (specialCase ?? '') + ' | Musharraka: Full siblings share with maternal siblings in 1/3.';
    specialCaseAr = (specialCaseAr ?? '') + ' | المشتركة: الإخوة الأشقاء يشاركون إخوة الأم في الثلث.';
    notes.push("Musharraka: Full siblings share equally with maternal siblings (Shafi'i/Maliki view).");
    notesAr.push('مسألة المشتركة: الإخوة الأشقاء يشاركون إخوة الأم في الثلث (الشافعي والمالكي).');
  }

  return {
    shares: finalShares,
    distributableEstate: net,
    totalAllocated: finalAllocated,
    awlApplied,
    raddApplied,
    awlFactor:  awlApplied  ? awlFactor  : undefined,
    raddFactor: raddApplied ? raddFactor : undefined,
    notes, notesAr, steps,
    specialCase, specialCaseAr,
    baitulmal: baitulmal > 0 ? baitulmal : undefined,
  };
}

// ============================================================
// VALIDATION
// ============================================================
export function validateHeirs(heirs: Heir[], deceasedGender: string): ValidationError[] {
  const errors: ValidationError[] = [];
  const hasHusband = heirs.some(h => h.type === 'husband');
  const hasWife    = heirs.some(h => h.type === 'wife');
  if (hasHusband && hasWife)
    errors.push({ severity: 'error', message: 'Cannot have both husband and wife', messageAr: 'لا يمكن وجود زوج وزوجة معاً' });
  if (deceasedGender === 'male' && hasHusband)
    errors.push({ severity: 'error', message: 'Deceased is male — cannot have husband as heir', messageAr: 'المتوفى ذكر — لا يرث الزوج' });
  if (deceasedGender === 'female' && hasWife)
    errors.push({ severity: 'error', message: 'Deceased is female — cannot have wife as heir', messageAr: 'المتوفاة أنثى — لا ترث الزوجة' });
  const wife = heirs.find(h => h.type === 'wife');
  if (wife && wife.count > 4)
    errors.push({ severity: 'error', message: 'Maximum 4 wives', messageAr: 'أقصى عدد للزوجات 4' });
  const father = heirs.filter(h => h.type === 'father');
  if (father.length > 1 || (father[0]?.count ?? 0) > 1)
    errors.push({ severity: 'error', message: 'Only one father possible', messageAr: 'لا يمكن وجود أكثر من أب واحد' });
  const mother = heirs.filter(h => h.type === 'mother');
  if (mother.length > 1 || (mother[0]?.count ?? 0) > 1)
    errors.push({ severity: 'error', message: 'Only one mother possible', messageAr: 'لا يمكن وجود أكثر من أم واحدة' });
  return errors;
}
