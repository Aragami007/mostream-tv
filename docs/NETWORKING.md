# Networking and security

## Same LAN

MoStream TV displays one active private IPv4 address and the selected TCP port. Friends on the same routed network enter that address in Syncplay along with the room and password.

If the connection fails, check:

1. Both computers are on the same LAN and client isolation is disabled.
2. The displayed address belongs to the correct Wi-Fi or Ethernet adapter.
3. Windows Firewall permits inbound TCP traffic for the server.
4. No other program is already listening on the selected port.

## Internet connections

Without a third-party relay, the host must be directly reachable. Usually this requires:

1. A router port-forward from the chosen external TCP port to the host's LAN address and port.
2. A stable LAN address for the host, normally through a DHCP reservation.
3. A Windows Firewall inbound rule for the chosen TCP port.
4. Sharing the host's public IP address and port with trusted friends.

Carrier-grade NAT, double NAT, and some institutional networks can prevent inbound hosting even with local configuration.

## Security limitations in 0.1.0

- TLS is not configured by the launcher.
- Router and firewall changes are not automated.
- The application does not discover or publish the host's public IP.
- The application does not provide a relay, tunnel, VPN, or dynamic-DNS service.
- The generated password protects entry but does not make a plaintext Internet connection private.

Use this release on trusted networks for testing. TLS certificate management should be completed before recommending direct public-Internet hosting.
