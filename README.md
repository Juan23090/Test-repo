## 🌳 Branching Strategy

We will use a strict branching hierarchy. **Direct pushes to `main` or `dev` are disabled.**

### 1. The Branch Hierarchy

| Branch | Environment | Purpose | Access |
| :--- | :--- | :--- | :--- |
| **`main`** | **Production** | Working and stable version. Reviewed bi-weekly | Roger/Gabe |
| **`dev`** | **DevTest** | Integration branch. PR from feature/fix | Git Coordinator/Team Lead |
| **`feature/*`** | **Local** | Active student work (e.g., `feature/donor-table`). | Students |
| **`fix/*`** | **Local** | Fixes for `dev` that made it through | All |

---

### 2. The Workflow (Step-by-Step)

1.  **Sync:** Start by pulling the latest changes to ensure you aren't building on old code:
    * `git checkout dev`
    * `git pull origin`
2.  **Branch:** Create your own workspace:
    * `git checkout -b feature/your-feature-name`
3.  **Build:** Work locally in your specific folder:
    * `/src/app` (Blazor WASM)
    * `/src/api` (Functions)
4.  **Push:** Send your work to GitHub:
    * `git push -u origin HEAD`
5.  **PR:** Open a **Pull Request (PR)** on GitHub from `dev ← your branch`.
6.  **Review:** The Team Lead/GitHub Coordinator reviews the code for logic and residency compliance.
7.  **Release:** Bi-weekly, `dev` is merged into `main` to update the live production site at `emailmoney.app`.


> **Pro-Tip for Students:** If your PR is large, break it down! Smaller PRs get reviewed faster.

--- 

**Last updated:** 06/10/2026 - Gabriel Pearse (gabe@pearse.name)
