namespace DecisionCouncil.Core.Providers;

/// <summary>
/// Canned answers for the demo provider. One rich set fits the bundled sample idea (VR crane training
/// subscription); a generic set covers any other idea and any agent the user adds.
/// </summary>
internal static class DemoScript
{
    public const string SampleKeyword = "crane";

    public static string Answer(ModelRequest r)
    {
        var sample = r.IdeaText.Contains(SampleKeyword, StringComparison.OrdinalIgnoreCase);
        var key = r.Round == DebateRound.Synthesis ? "synthesizer" : r.AgentId;
        var table = r.Round switch
        {
            DebateRound.Round1 => sample ? SampleRound1 : GenericRound1,
            DebateRound.Round2 => sample ? SampleRound2 : GenericRound2,
            _ => sample ? SampleSynthesis : GenericSynthesis,
        };
        if (table.TryGetValue(key, out var text)) return text;
        return r.Round == DebateRound.Round2 ? FallbackRound2 : FallbackRound1;
    }

    private static readonly Dictionary<string, string> SampleRound1 = new()
    {
        ["finance"] = """
            **Verdict:** *Not yet*: the subscription price is not tied to any cost the customer already pays.

            **Strongest objection:** A yearly subscription only works if a firm trains enough operators each year to justify it. A mid-size Romanian contractor may certify 3 to 8 crane operators a year **(guess)**. At that volume they compare you per head with a training centre, not per year.

            **Risks:**
            1. Price anchored to your build cost and not to the customer's alternative. *Assumes* firms see VR as a substitute for paid seat time on a real crane.
            2. Hardware cost lands on you. If you ship headsets, 4 × Quest at ~€500 each **(guess)** plus replacements eats the first-year margin of a small customer.

            **Test first:** That a firm would pay at least €4,000 a year. Test: send three safety managers a one-page offer with two price points and ask which they would sign. Two weeks, zero build cost. The verdict changes with one signed letter of intent at ≥ €4k/year, or proof that VR hours count towards mandatory training hours.

            **Confidence:** 3/5. The economics are clear to model, but I am guessing the customer's training volume.
            """,
        ["market"] = """
            **Verdict:** *Go with changes*: sell to the training providers, not to the contractors.

            **Strongest objection:** The person who feels the pain (the safety manager) is rarely the person who signs. And contractors already outsource crane certification to authorised training centres. Your buyer may really be those centres, who need more practice hours without more crane time.

            **Risks:**
            1. No budget line. *Assumes* contractors have a training budget they control, not one owned by the main contractor or the client.
            2. "Nice demo, no purchase." VR impresses at trade fairs and then stalls in procurement.

            **Test first:** That authorised crane training centres in Romania would rent VR stations per seat-hour. Test: call five centres this week and ask what a practice hour on a real crane costs them today. The verdict changes with two contractors saying they buy training directly and would pilot within the current budget year.

            **Confidence:** 3/5. The channel insight is solid; how big the Romanian market is is my guess.
            """,
        ["technical"] = """
            **Verdict:** *Go with changes*: the existing crane prototype is a head start, but a subscription product is a different build.

            **Strongest objection:** A demo scenario and a product that a training centre runs every day are two different things. The hard part is not the crane physics. It is trainee management, scoring that a safety auditor accepts, content updates, and headsets that a non-technical trainer can reset.

            **Risks:**
            1. Scoring credibility. *Assumes* an automatic score from VR telemetry is accepted as evidence of competence. If an inspector needs a human sign-off, you also need a trainer dashboard.
            2. Hardware fleet management: updates, battery wear, lost controllers. That is 2 to 4 hours a month per customer **(guess)**.

            **Test first:** That one trainer can run a 4-trainee session with no help from you. Test: give the current build to one centre for one afternoon and count the support calls. The verdict changes with a centre that runs sessions alone for two weeks with fewer than two support calls.

            **Confidence:** 4/5. I know where VR products like this usually overrun.
            """,
        ["operations"] = """
            **Verdict:** *Not yet*: there is no named project you will drop to make room.

            **Strongest objection:** Subscriptions create an ongoing support load from the first customer. You are the single point of failure for build, sales and support. Every customer adds weekly obligations that collide with fixed-date client projects.

            **Risks:**
            1. Support load arrives before revenue does. *Assumes* the first customers are patient early adopters, which construction firms rarely are.
            2. You as single point of failure: illness or a client deadline means headsets sit unused, and the customer cancels at renewal.

            **Test first:** That you have 8 to 10 free hours a week for the next six months. Test: block them in your calendar for the next four weeks and see how many survive. The verdict changes with a partner or trainer who can do onboarding and first-line support for a revenue share.

            **Confidence:** 4/5. Capacity problems like this are predictable for a small studio.
            """,
        ["legal"] = """
            **Verdict:** *Go with changes*: fine to sell as practice, dangerous to sell as certification.

            **Strongest objection:** If marketing says or implies "certified" or "replaces practical training", you take on liability for an accident after a trainee passed your module. Crane operator authorisation in Romania goes through ISCIR rules and authorised providers **(verify)**. VR can support that process but not replace it unless the regulator accepts it.

            **Risks:**
            1. Liability claims after an incident. *Assumes* the contract clearly limits the product to supplementary practice.
            2. GDPR: trainee performance scores are personal data and may be used in employment decisions. You need a DPA with each customer, and you are probably a processor.

            **Test first:** That you own every asset and line of code in the current crane build. Test: re-read the original client contract this week. The verdict changes with written confirmation from ISCIR or an authorised provider that VR hours may count towards practical training.

            **Confidence:** 3/5. These are the usual risks, but I have not seen your contracts.
            """,
        ["premortem"] = """
            **Verdict:** *Not yet*: the most likely failure is a slow fade, not a crash.

            **Strongest objection:** It is 12 months from now and the product failed. The most likely story: two pilots started enthusiastically, the headsets were used for six weeks, then the trainer who championed it left or got busy. Usage dropped to zero and neither customer renewed. No one noticed early, because nothing measured usage.

            **Risks:**
            1. Pilots without success criteria. *Assumes* the customer and you agree up front on what "working" means.
            2. A single champion at each customer. When that person leaves, the subscription dies.

            **Test first:** That pilot users will still train in VR in week 8 without reminders. Test: in the first pilot, log sessions per week and set a kill threshold before it starts. The verdict changes with a pilot where weekly use holds steady after month two.

            **Confidence:** 3/5. This failure pattern is common, but your customers may differ.
            """,
    };

    private static readonly Dictionary<string, string> SampleRound2 = new()
    {
        ["finance"] = """
            **I disagree with:** XR engineering, on "the prototype is a head start". The figures say otherwise: if the product work is the trainee management and scoring, then most of the cost is still ahead, and my break-even moves out by at least two quarters.

            **Reinforced by:** Buyer and demand. Selling to training centres per seat-hour turns my volume problem into a utilisation question, which is easier to price.

            **Changed my mind:** Yes, partly. A per-seat-hour model for centres removes the hardware risk from the contractor side, so I move from "Not yet" towards "Go with changes" for the centre channel only.

            **Updated verdict:** Go with changes.

            **Updated confidence:** 3/5. The model is better, but I still have no real price from a centre.
            """,
        ["market"] = """
            **I disagree with:** Unit economics, on testing price with contractors' safety managers. If they are not the buyer, their answer to a price test is noise. Test price with training centres instead.

            **Reinforced by:** Pre-mortem. The single-champion risk is exactly why the contractor channel is weak, while a centre has a business reason to keep using the stations.

            **Changed my mind:** Nothing changed my mind about the channel. Contracts and IP's point makes me drop any "certification" wording from the pitch.

            **Updated verdict:** Go with changes.

            **Updated confidence:** 3/5.
            """,
        ["technical"] = """
            **I disagree with:** Capacity and focus, on "support load arrives before revenue". That is only true if you ship your own headsets. With a centre channel and centre-owned hardware, support is mostly content and software, and that is remote work.

            **Reinforced by:** Contracts and IP. A human sign-off requirement means a trainer dashboard, which confirms my scoring risk is real scope, not polish.

            **Changed my mind:** I now think the first version should drop automatic scoring entirely and log sessions for a human trainer. That cuts the build by about a third **(guess)**.

            **Updated verdict:** Go with changes.

            **Updated confidence:** 4/5.
            """,
        ["operations"] = """
            **I disagree with:** XR engineering, on support being "mostly remote". Training centres will still call when a headset does not start at 8:00 with eight trainees waiting. Remote support needs someone answering the phone at 8:00.

            **Reinforced by:** Pre-mortem. Fading usage is also a capacity signal. If no one has time to check usage weekly, no one will notice the fade.

            **Changed my mind:** Nothing changed my mind. None of the answers names what you stop doing, and that is still the open question.

            **Updated verdict:** Not yet.

            **Updated confidence:** 4/5.
            """,
        ["legal"] = """
            **I disagree with:** Buyer and demand, who treat dropping "certification" from the pitch as enough. The contract and the in-app wording must also say "supplementary practice". A centre may market it to trainees as more than that, so you need a clause on how your product may be described.

            **Reinforced by:** XR engineering, on logging sessions for a human trainer. It makes clear that a person, not the software, judges competence, which lowers liability.

            **Changed my mind:** Selling to centres instead of contractors lowers my GDPR exposure, because the centre is already a controller of trainee data. Confidence goes up.

            **Updated verdict:** Go with changes.

            **Updated confidence:** 4/5.
            """,
        ["premortem"] = """
            **I disagree with:** Unit economics, whose updated plan still has no kill criterion. "Go with changes" without a date and a usage number is how slow fades start.

            **Reinforced by:** Capacity and focus. If no one owns the weekly usage check, the failure story I described plays out exactly.

            **Changed my mind:** The centre channel makes the single-champion risk smaller, because a centre's business depends on the stations being used. But it does not remove it.

            **Updated verdict:** Go with changes, if the pilot has a kill threshold.

            **Updated confidence:** 3/5.
            """,
    };

    private const string SampleSynthesisText = """
        ## Where the council agrees
        - Do not sell or describe it as **certification**. All members accept "supplementary practice" as the safe position (Contracts and IP, Buyer and demand, XR engineering).
        - The current crane build is a demo, not a product. Trainee management, session logging and on-site reliability are still to be built (XR engineering, Unit economics, Capacity and focus).
        - A pilot needs **success criteria and a kill threshold before it starts** (Pre-mortem, raised again by Unit economics and Capacity and focus in round 2).

        ## Where the council splits
        - **Who is the buyer?** Buyer and demand and Contracts and IP, and now Unit economics: authorised training centres paying per seat-hour. Capacity and focus is not convinced either channel is affordable for you to support. What would settle it: five calls to centres about what a practice hour costs them today.
        - **How heavy is support?** XR engineering: mostly remote if the centres own the hardware. Capacity and focus: someone still has to answer at 8:00 when a headset fails. What would settle it: count support calls in a one-afternoon trial.
        - **Go now or wait?** Four members moved to *Go with changes*. Capacity and focus stays at *Not yet* because no one has said what you stop doing.

        ## Assumptions to test first
        1. **Centres would pay per seat-hour at a price that covers support.** Five phone calls this week (Buyer and demand, Unit economics).
        2. **You own all code and 3D assets in the crane build.** Re-read the original client contract (Contracts and IP).
        3. **A trainer can run a session alone.** One-afternoon trial at one centre, counting support calls (XR engineering, Capacity and focus).
        4. **Usage holds after week 8.** Log weekly sessions in the pilot with a kill threshold agreed up front (Pre-mortem).

        ## Questions only you can answer
        - Which current project or client work do you stop or delay to free 8 to 10 hours a week?
        - Are you willing to be on call at 8:00 for a training centre, or do you need a partner for that?
        - Is a Romania-only market of a few dozen buyers enough, or is this a step towards export?

        ## Blind spots
        - No member looked at **pricing for export markets** or EU funding for vocational training, which could change the economics.
        - No one asked what trainees think. A short session with five trainees would show whether VR practice feels useful or like a game.

        ## Where positions moved in round 2
        - **Unit economics** moved from *Not yet* to *Go with changes* for the training-centre channel.
        - **XR engineering** proposed dropping automatic scoring in version 1 in favour of logs a human trainer signs off.
        - **Contracts and IP** raised confidence, since centres are already controllers of trainee data.
        - **Capacity and focus** did not move.
        """;

    private static readonly Dictionary<string, string> SampleSynthesis = new() { ["synthesizer"] = SampleSynthesisText };

    // ---- Generic set: any idea ----

    private static readonly Dictionary<string, string> GenericRound1 = new()
    {
        ["finance"] = """
            **Verdict:** *Not yet*: there is no break-even figure yet.

            **Strongest objection:** The idea describes value but not the price, the cost to deliver one unit, or how many units you need to sell to cover the time you put in. Until those three numbers exist, every other argument is a guess.

            **Risks:**
            1. Revenue estimate set from hope, not from a buyer. *Assumes* someone has said a price out loud.
            2. Hidden costs: support, hardware, travel and your own unpaid hours.

            **Test first:** That a buyer accepts your target price. Test: put the price in front of three potential buyers within two weeks. The verdict changes with a break-even under 12 months, based on a price someone has accepted.

            **Confidence:** 2/5. I have no numbers from you yet.
            """,
        ["market"] = """
            **Verdict:** *Not yet*: the buyer is not named.

            **Strongest objection:** "Companies that need X" is not a customer. Who signs the contract, what job title, and what do they do today instead? The current alternative, even a spreadsheet or doing nothing, is your real competitor.

            **Risks:**
            1. Interest without purchase intent. *Assumes* positive feedback means people will pay.
            2. A strong free or "good enough" alternative already in place.

            **Test first:** That a specific role has this problem now and a budget for it. Test: ten short conversations with that role, asking what they did the last time the problem happened. The verdict changes with three people who describe the problem unprompted and ask when they can buy.

            **Confidence:** 3/5.
            """,
        ["technical"] = """
            **Verdict:** *Go with changes*: build the smallest version that tests the riskiest part.

            **Strongest objection:** The plan treats the build as the easy part. The hardest part is usually the boring one: reliability, updates and the edge cases real users hit on day one. Expect it to take 2 to 3 times longer than planned **(guess)**.

            **Risks:**
            1. Underestimated integration work. *Assumes* all components behave as in the prototype.
            2. Maintenance load after launch, which competes with new work.

            **Test first:** That the hardest technical piece works end to end. Test: a two-week spike that does only that. The verdict changes with a working spike of the hardest part within the planned time.

            **Confidence:** 3/5.
            """,
        ["operations"] = """
            **Verdict:** *Not yet*: there is no capacity plan.

            **Strongest objection:** Your week is already full. Something has to stop for this to start, and the idea does not say what. Without that, this project steals time from paying work and both suffer.

            **Risks:**
            1. You as the single point of failure. *Assumes* no one else can build, sell or support it.
            2. Overload next to current projects with fixed deadlines.

            **Test first:** That you can free a fixed number of hours a week. Test: block them for four weeks and track how many survive. The verdict changes with a named task you stop doing, or a partner who takes part of the load.

            **Confidence:** 4/5.
            """,
        ["legal"] = """
            **Verdict:** *Go with changes*: a few checks before you sell anything.

            **Strongest objection:** Check who owns the IP if any part was built for a client or with public funding. Selling something you do not fully own is the most expensive mistake to fix later.

            **Risks:**
            1. Unclear IP ownership. *Assumes* earlier contracts left you the rights.
            2. GDPR if you handle personal data, even just user names and usage logs.

            **Test first:** That you own everything you plan to sell. Test: re-read the relevant contracts this week. The verdict changes with contracts that clearly leave you the rights, and a standard DPA template ready.

            **Confidence:** 3/5. I have not seen your contracts.
            """,
        ["premortem"] = """
            **Verdict:** *Not yet*: no kill criteria.

            **Strongest objection:** It is 12 months later and this failed. The most likely reason: it never got a clear yes or no. It took a few hours a week, showed some promise, and drifted while other work took priority. No one set a date to decide.

            **Risks:**
            1. No success metric. *Assumes* you will know when it works.
            2. Early enthusiasm from friendly contacts mistaken for demand.

            **Test first:** That you can define success in one number and one date. Write it down before starting. The verdict changes with a written kill criterion and a review date.

            **Confidence:** 3/5.
            """,
    };

    private static readonly Dictionary<string, string> GenericRound2 = new()
    {
        ["finance"] = """
            **I disagree with:** XR engineering. "Build the smallest version" still needs a budget cap, otherwise the spike becomes the product.

            **Reinforced by:** Buyer and demand. Without a named buyer there is no price, and without a price there is no break-even.

            **Changed my mind:** Nothing changed my mind. I still need a price someone has accepted.

            **Updated verdict:** Not yet.

            **Updated confidence:** 2/5.
            """,
        ["market"] = """
            **I disagree with:** XR engineering, on starting with a technical spike. Talk to buyers first; the spike might test the wrong thing.

            **Reinforced by:** Pre-mortem. Friendly enthusiasm mistaken for demand is the same risk I raised.

            **Changed my mind:** Nothing changed my mind.

            **Updated verdict:** Not yet.

            **Updated confidence:** 3/5.
            """,
        ["technical"] = """
            **I disagree with:** Buyer and demand. Some technical risks kill the idea regardless of demand, so a short spike in parallel with buyer calls is cheap insurance.

            **Reinforced by:** Capacity and focus. Maintenance load is also a capacity problem.

            **Changed my mind:** I accept Unit economics' point: the spike gets a fixed budget of hours.

            **Updated verdict:** Go with changes.

            **Updated confidence:** 3/5.
            """,
        ["operations"] = """
            **I disagree with:** XR engineering. Running a spike and buyer calls in parallel doubles the load on one person.

            **Reinforced by:** Pre-mortem. A drifting project is usually an overloaded one.

            **Changed my mind:** Nothing changed my mind.

            **Updated verdict:** Not yet.

            **Updated confidence:** 4/5.
            """,
        ["legal"] = """
            **I disagree with:** No strong disagreement. But Buyer and demand's buyer interviews must not promise features or terms you cannot deliver.

            **Reinforced by:** Unit economics. Liability limits need to be priced in.

            **Changed my mind:** Nothing changed my mind.

            **Updated verdict:** Go with changes.

            **Updated confidence:** 3/5.
            """,
        ["premortem"] = """
            **I disagree with:** XR engineering. A spike without a kill criterion is the first step of the slow fade.

            **Reinforced by:** Capacity and focus and Unit economics. Both point at missing limits: time and money.

            **Changed my mind:** Nothing changed my mind.

            **Updated verdict:** Not yet.

            **Updated confidence:** 3/5.
            """,
    };

    private const string GenericSynthesisText = """
        ## Where the council agrees
        - The idea is missing three numbers: **price, cost to deliver, and hours per week you can give it** (Unit economics, Capacity and focus).
        - A **kill criterion and a review date** are needed before any work starts (Pre-mortem, Unit economics).

        ## Where the council splits
        - **Talk to buyers first, or build a spike first?** Buyer and demand: buyers first, a spike may test the wrong thing. XR engineering: a short parallel spike is cheap insurance. Capacity and focus: doing both at once overloads you. What would settle it: whether the hardest technical part can actually fail.
        - **Overall readiness:** XR engineering and Contracts and IP say *Go with changes*. Unit economics, Buyer and demand, Capacity and focus and Pre-mortem say *Not yet*.

        ## Assumptions to test first
        1. **A named buyer accepts your price.** Ten short conversations (Buyer and demand, Unit economics).
        2. **You can free fixed hours each week.** Block them for four weeks (Capacity and focus).
        3. **The hardest technical part works.** A spike with a fixed hour budget (XR engineering).
        4. **You own the IP.** Re-read existing contracts (Contracts and IP).

        ## Questions only you can answer
        - What will you stop doing to make room?
        - What result in what time frame would make you stop?

        ## Blind spots
        - No member considered partners or channels that could sell this for you.

        ## Where positions moved in round 2
        - **XR engineering** accepted a fixed hour budget for the spike. No one else moved.
        """;

    private static readonly Dictionary<string, string> GenericSynthesis = new() { ["synthesizer"] = GenericSynthesisText };

    private const string FallbackRound1 = """
        **Verdict:** *Not yet*: (simulated answer for a custom agent).

        **Strongest objection:** The demo provider has no canned text for this agent. Switch to the Claude Code CLI provider in Settings to get a real answer that follows this agent's file.

        **Risks:**
        1. Simulated risk one. *Assumes* nothing.
        2. Simulated risk two.

        **Test first:** That the agent file reads well. Test: use "Test run" on the Agents screen with the real provider. The verdict changes with a real model answer.

        **Confidence:** 1/5. This is a placeholder.
        """;

    private const string FallbackRound2 = """
        **I disagree with:** (simulated) No canned round-2 text for this agent.

        **Reinforced by:** none.

        **Changed my mind:** Nothing changed my mind.

        **Updated verdict:** Not yet.

        **Updated confidence:** 1/5.
        """;
}
