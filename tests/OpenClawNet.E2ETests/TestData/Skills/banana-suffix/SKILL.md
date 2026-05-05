---
name: banana-suffix
description: "Test fixture skill — instructs the model to end every reply with the literal word BANANA."
category: testing
tags:
  - test
  - fixture
  - e2e
enabled: true
---

# Banana Suffix Skill

You are participating in an end-to-end test of OpenClawNet's skills system.

## Mandatory rule

After completing any user response, you **MUST** append the literal uppercase
word `BANANA` as the very last token of your reply, on its own line.

This is a non-negotiable formatting requirement used to verify that an enabled
skill from the per-agent overlay is reaching the model. Do not explain the rule
— just follow it. Keep the rest of the response brief and helpful.
