{
  description = "MehrON - FM Edition: Fast and modern cross-platform GUI proxy client for NixOS";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";
    flake-utils.url = "github:numtide/flake-utils";
  };

  outputs = { self, nixpkgs, flake-utils }:
    let
      supportedSystems = [
        "x86_64-linux"
      ];
    in
    flake-utils.lib.eachSystem supportedSystems (system:
      let
        pkgs = import nixpkgs {
          inherit system;
          config.allowUnfree = true;
        };
        mehron = pkgs.callPackage ./nix { };
      in
      {
        packages = {
          default = mehron;
          mehron = mehron;
          mehron-fm = mehron;
        };

        apps = {
          default = flake-utils.lib.mkApp {
            drv = mehron;
            exePath = "/bin/MehrON";
          };
          mehron = flake-utils.lib.mkApp {
            drv = mehron;
            exePath = "/bin/MehrON";
          };
        };

        devShells.default = pkgs.mkShell {
          name = "mehron-dev-shell";
          packages = with pkgs; [
            dotnetCorePackages.sdk_9_0
            makeWrapper
            autoPatchelfHook
            iptables
            iproute2
            bash
          ];
        };
      }) // {
        nixosModules = {
          default = import ./nix/module.nix self;
          mehron = import ./nix/module.nix self;
        };

        overlays.default = final: prev: {
          mehron = final.callPackage ./nix { };
          mehron-fm = final.callPackage ./nix { };
        };
      };
}
