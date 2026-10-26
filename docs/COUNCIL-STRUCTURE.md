# Council structure

The council is organised in **6 categories**. Each category has a **lead**, a generalist used in quick checks, and **specialists**, each narrowed to one field. A debate should use about 5–7 agents, picked by preset: in round 2 every agent reads every other answer, so cost and prompt length grow with the square of the agent count.

Agreed on 2026-10-26. The agents are improved one at a time after the app supports categories.

## Agents

| Category | Agent | id | Lead | Lens |
|---|---|---|---|---|
| Finance | Unit economics | `finance` | yes | Price, cost per customer, break-even |
| | Cash and runway | `finance-cash` | | Cash gap, payment terms, slow public payers, how long the money lasts |
| | Grants and funding | `finance-funding` | | EU and national grants, co-financing, conditions on IP and reporting |
| Market | Buyer and demand | `market` | yes | Who signs, what they do today instead, why now |
| | Healthcare buyer | `market-healthcare` | | Hospitals, sterilization departments, medical and nursing schools, hospital procurement |
| | Training buyer | `market-training` | | Corporate training (L&D), health and safety, onboarding |
| | Culture and tourism buyer | `market-culture` | | Museums, galleries, festivals, tourism boards, cultural funding |
| | Partners and channels | `market-partners` | | Agencies, resellers, white-label and subcontracted work |
| Technical | XR engineering | `technical` | yes | Feasibility, effort, headset performance, maintenance |
| | 3D art and content | `tech-art` | | Asset effort, art pipeline, content updates |
| | Learning design | `tech-learning` | | Whether the training teaches, assessment validity, learning outcomes |
| | Deployment and devices | `tech-deploy` | | Headset fleets, client IT and security, offline use, installations running all day |
| Operations | Capacity and focus | `operations` | yes | Hours, single points of failure, what gets dropped |
| | Delivery and scope | `ops-delivery` | | Fixed-price risk, scope creep, acceptance and sign-off |
| Legal | Contracts and IP | `legal` | yes | Client contracts, licensing, IP ownership, liability |
| | Privacy | `legal-privacy` | | GDPR, health data, visitor and trainee data |
| | Sector rules | `legal-sector` | | Medical-device claims (MDR), public procurement, drone rules, accessibility |
| | Tax and company | `legal-tax` | | Company tax regime, VAT, selling outside the EU |
| Strategy and risk | Pre-mortem | `premortem` | yes | Assume it failed in 12 months |
| | Strategist | `strategy` | | Fit with the plan, opportunity cost, focus |
| | Customer voice | `customer-voice` | | Plays the buyer reacting to the pitch |

The 6 leads keep their current ids, so past debates and presets stay valid.

## Rules for the agent files

- **Shipped in the app defaults** (`defaults/agents/`), so every install gets them. Specialists are written for a field, not for one company: anything specific to the owner (country, team, products) comes from `context.md`.
- New front matter key `category:` with one of: `Finance`, `Market`, `Technical`, `Operations`, `Legal`, `Strategy and risk`. Agents without it go under "Other".
- `order` sets the place inside a category: lead first. Category order is fixed by the list above.
- File names: keep `01-finance.md` … `06-premortem.md` for the leads; specialists are `NN-<id>.md` with `NN` from 07 up.
- Avatars: the 7 existing shapes (crab, antenna, robot, cat, owl, ghost, dib). Specialists share their category lead's colour family.

## Presets

| Preset | Agents |
|---|---|
| Quick check | The 5 leads + Pre-mortem |
| Healthcare idea | Unit economics, Healthcare buyer, XR engineering, Learning design, Deployment and devices, Sector rules, Pre-mortem |
| Culture and tourism idea | Unit economics, Grants and funding, Culture and tourism buyer, 3D art and content, Deployment and devices, Capacity and focus, Pre-mortem |
| Partner deal | Unit economics, Cash and runway, Partners and channels, Delivery and scope, Contracts and IP, Strategist |
| Product build | Unit economics, Buyer and demand, XR engineering, 3D art and content, Capacity and focus, Strategist, Pre-mortem |
| Full council | Every enabled agent (expensive; for big, one-off decisions) |

## App changes this needs

1. `AgentDefinition.Category` and the `category:` key in `AgentLoader`, plus a `category:` line in `NewAgentTemplate`.
2. Group by category on the New idea page and the Agents page.
3. **Seeding new default agents into existing installs.** Today `DataFolder.EnsureSeeded` only copies agents into an empty agents folder, so existing users (including this machine) would never get the 15 specialists. It needs to remember which default agents it has already offered, and copy only the new ones, so an agent the user deleted still does not come back.
4. The new presets in `AppSettings` defaults, and added to existing `settings.json` files without touching presets the user made.

## Order of work

1. App changes above.
2. Add the 15 specialist files, with a first draft each.
3. Improve each agent one at a time, lead first in each category.
