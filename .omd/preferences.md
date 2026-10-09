---
schema: omd.preferences/v1
design_md_hash_at_creation:
---

# Preference Log

## 2026-09-22T08:07:41.000Z — minimap-backgrounds-stay-translucent

```omd-meta
id: pref_muce71y1_3168e501
timestamp: 2026-09-22T08:07:41.000Z
scope: components.minimap
signal: user-correction
confidence: explicit
status: pending
source_agent: Codex
source_context: "Assets/Prefabs/UI/MiniMap.prefab"
```

Keep minimap backgrounds translucent so they do not obscure the gameplay view.

## 2026-09-28T13:40:00.000Z — inventory-drag-and-existing-art

```omd-meta
id: pref_inventory_0928
timestamp: 2026-09-28T13:40:00.000Z
scope: components.inventory
signal: user-correction
confidence: explicit
status: pending
source_agent: Codex
source_context: "PouchUI / HandSlotSelectionUI prefabs"
```

Reuse the project's pixel artwork; do not generate art. Keep UI layouts editable in prefabs. Inventory, dragged items and world pickups must share the same item icon; minion acquisition uses the minion portrait, not its skill icon. Hold progress fills the whole rectangular button. Dragging outside the bag shows a red outline; releasing drops at the pointer's nearest walkable world position, with no bottom staging bar.

## 2026-10-08T15:26:32.060Z — c-and-v-are-independent-panels

```omd-meta
id: pref_muzovtrw_076b1e90
timestamp: 2026-10-08T15:26:32.060Z
scope: components.inventory
signal: user-correction
confidence: explicit
status: pending
source_agent: Codex
source_context: "SkillExplainUI / PouchUI / UIPopUpManager"
```

C toggles only the left stats sidebar; its top arrow independently expands equipment, guard and minion details. V toggles the center inventory and right set-effects panel. Both can remain open, with expanded C details above V regardless of opening order. Preserve editor-authored prefabs and existing colors; use simple shapes for missing art.
