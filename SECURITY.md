# Security policy

## Supported version

Version 0.1.x receives security fixes while it is the current development line.

## Current security posture

Mostream TV 0.1.0 is intended for trusted LAN testing. It does not yet configure TLS, router port forwarding, Windows Firewall, authentication rate limiting, or automatic updates. Do not expose it directly to the Internet unless you understand and accept those limitations.

The server password is passed to the Syncplay server through a child-process environment variable. The local Syncplay client receives it through its documented command-line interface, which may make it visible to other sufficiently privileged local processes.

## Reporting a vulnerability

Use GitHub's private security-advisory reporting for this repository. Do not publish passwords, public IP addresses, or proof-of-concept details in a public issue. Include reproduction steps, affected version, impact, and any proposed mitigation.
