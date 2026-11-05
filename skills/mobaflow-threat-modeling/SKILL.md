---
name: mobaflow-threat-modeling
description: Model security risks of scoped MOBAflow changes to authentication, external inputs, network exposure, dependency reachability, or railway commands.
---

# MOBAflow threat modeling

Use the assigned issue, design, or exact PR comparison as the boundary. Follow the workspace secrets protocol. Trace affected inputs and commands from source to destination. Identify assets, trust boundaries, authentication and authorization checks, and possible consequences. Include MOBApi, MOBAsmart, local runtime, and device gateways only where the change touches them.

For each material scenario, give preconditions, the reachable path, impact, existing controls, and evidence. Distinguish a scanner match from demonstrated application reachability. Label assumptions and unknowns. Describe potential physical consequences separately when railway commands are involved.

Return prioritized findings, proposed mitigations, and a verification method or negative test to the assigned Quality owner. Route cross-team decisions through the Project Manager. Live probing, equipment commands, code changes, publication, and acceptance of residual risk each require authorization within the assignment.

Method reference: [OWASP Threat Modeling](https://community.owasp.org/Threat_Modeling).
