self: { config, lib, pkgs, ... }:

with lib;

let
  cfg = config.programs.mehron;
  defaultPackage = self.packages.${pkgs.system}.default or (pkgs.callPackage ./default.nix { });
in
{
  options.programs.mehron = {
    enable = mkEnableOption "MehrON - FM Edition GUI proxy client";

    package = mkOption {
      type = types.package;
      default = defaultPackage;
      defaultText = literalExpression "pkgs.mehron";
      description = "The MehrON package to install.";
    };

    tunMode = mkOption {
      type = types.bool;
      default = true;
      description = ''
        Whether to enable capabilities and networking permissions for seamless TUN mode routing.
      '';
    };
  };

  config = mkIf cfg.enable {
    environment.systemPackages = [ cfg.package ];

    # Optional networking requirements for TUN mode
    boot.kernelModules = mkIf cfg.tunMode [ "tun" ];

    # Ensure iptables / ip tools and bash are available system-wide for proxying
    programs.bash.enable = true;
  };
}
