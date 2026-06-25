# agents/

This folder is where the **agent implementation code** lives — the runnable workers behind RouteOps' five-agent system.

## The five agents

1. **Calendar Scout** — reads Google Calendar, validates/geocodes locations, builds the ordered day graph.
2. **Route Engineer** — computes legs, selects travel modes, derives leave-by, and compares baseline vs anomaly via Google Routes.
3. **Horizon Watcher** — ingests TfL / weather / news disruptions, trust-scores clusters, and attributes causes to legs.
4. **Comms Dispatcher** — turns routing decisions into WhatsApp leave-by alerts (via Wassist) while avoiding spam.
5. **Orchestrator** — runs the 5-minute loop, delegates to the agents, merges state in Supabase, and escalates when the day breaks.

See [`../docs/12-five-agents.md`](../docs/12-five-agents.md) for the full spec: inputs/outputs, steps, self-evaluation checks, self-healing playbook, and the orchestration diagram.
