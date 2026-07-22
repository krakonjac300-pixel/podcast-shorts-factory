# QA audit - 20 human-labelled clip(s)

- false accepts (agent PASSED a clip a human calls broken): 1/20
- false rejects (agent FLAGGED a clean clip): 0/20

## False accepts - the ones that matter
- clip 73 (rules vpre-3): frozen_or_black

A false-accept rate above zero on captions_on_face, frozen_or_black or bad_crop means the gate is not yet safe to trust unattended for that class.
