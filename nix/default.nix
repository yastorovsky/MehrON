{ lib
, stdenv
, fetchurl
, makeWrapper
, autoPatchelfHook
, zlib
, openssl
, icu
, fontconfig
, freetype
, xorg
, libxkbcommon
, wayland
, libGL
, libglvnd
, glib
, gtk3
, dbus
, iptables
, iproute2
, bash
, sudo
, coreutils
}:

let
  pname = "mehron";
  version = "7.25.59";

  runtimeLibs = [
    zlib
    openssl
    icu
    fontconfig
    freetype
    xorg.libX11
    xorg.libXcursor
    xorg.libXext
    xorg.libXi
    xorg.libXrandr
    xorg.libXrender
    xorg.libXtst
    xorg.libSM
    xorg.libICE
    libxkbcommon
    wayland
    libGL
    libglvnd
    glib
    gtk3
    dbus
    stdenv.cc.cc.lib
  ];

  runtimeBinaries = [
    iptables
    iproute2
    bash
    sudo
    coreutils
  ];
in
stdenv.mkDerivation rec {
  inherit pname version;

  src = fetchurl {
    url = "https://github.com/yastorovsky/MehrON/releases/download/v${version}-beta/MehrON-FM-Edition-nixos-64.tar.gz";
    hash = "sha256-DjNY93dyIzTkdQqokJVlbv3LosANh986b45gRC1w83s=";
  };

  nativeBuildInputs = [
    makeWrapper
    autoPatchelfHook
  ];

  buildInputs = runtimeLibs;

  installPhase = ''
    runHook preInstall

    mkdir -p $out/opt/MehrON $out/bin $out/share/applications $out/share/pixmaps $out/share/icons/hicolor/512x512/apps

    cp -rf ./* $out/opt/MehrON/
    chmod +x $out/opt/MehrON/MehrON $out/opt/MehrON/AmazTool || true

    # Install desktop launcher
    cat <<EOF > $out/share/applications/MehrON.desktop
[Desktop Entry]
Name=MehrON (FM Edition)
Comment=MehrON Proxy Client - FM Edition
Exec=$out/bin/MehrON %u
Icon=MehrON
Terminal=false
Type=Application
Categories=Network;Proxy;
StartupNotify=true
StartupWMClass=MehrON
EOF

    # Install icons if present
    if [ -f "$out/opt/MehrON/MehrON-logo.png" ]; then
      cp -f "$out/opt/MehrON/MehrON-logo.png" "$out/share/pixmaps/MehrON.png"
      cp -f "$out/opt/MehrON/MehrON-logo.png" "$out/share/icons/hicolor/512x512/apps/MehrON.png"
    fi

    # Install launcher wrapper with full PATH and LD_LIBRARY_PATH
    makeWrapper $out/opt/MehrON/MehrON $out/bin/MehrON \
      --prefix PATH : ${lib.makeBinPath runtimeBinaries} \
      --prefix LD_LIBRARY_PATH : ${lib.makeLibraryPath runtimeLibs} \
      --set DOTNET_SYSTEM_GLOBALIZATION_INVARIANT 0

    # Convenient lowercase alias
    ln -s $out/bin/MehrON $out/bin/mehron

    runHook postInstall
  '';

  meta = with lib; {
    description = "MehrON - FM Edition: High-performance cross-platform GUI proxy client for NixOS";
    homepage = "https://github.com/yastorovsky/MehrON";
    license = licenses.gpl3Plus;
    platforms = [ "x86_64-linux" ];
    mainProgram = "MehrON";
  };
}
