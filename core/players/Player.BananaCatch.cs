using com.forerunnergames.energyshot.weapons;
using Godot;

namespace com.forerunnergames.energyshot.players;

// Catching a live banana in the slingshot (issue #251, thepro & Caleb): only while
// you're actively DRAWING when it lands on you - merely holding a slingshot is not
// enough. The catch nocks the grenade as cosmetic ammo (bananas aren't a capped
// item, so no server escrow) with its fuse STILL TICKING - a hot potato: fire it back
// fast, or it goes off in your pouch. You can catch your own. Drawing replicates so
// the shooter's live banana (& every peer's cosmetic copy) sees the catch the same way.
public partial class Player
{
  [Export] public bool DrawingSlingshot { get; set; }
  private float _grenadeFuseLeft;

  private bool HoldsGrenade => SlingshotAmmo == HeldWeapon.BananaGrenade;

  // Called each physics frame from UpdateSlingshot's caller: keep the replicated flag
  // honest & tick the pouch fuse.
  private void UpdateBananaCatch (double delta)
  {
    var drawing = _slingshotDrawSeconds > 0.0f && IsSlingshotSelected && HasSlingshot && !Fallen;
    if (drawing != DrawingSlingshot) DrawingSlingshot = drawing;
    if (!HoldsGrenade || !IsMultiplayerAuthority()) return;
    _grenadeFuseLeft -= (float)delta;
    if (_grenadeFuseLeft <= 0.0f) GrenadeGoesOffInThePouch();
  }

  // The shooter's live banana met our drawn slingshot: it tells us (the shooter only
  // reports, like every hit) & we nock it with whatever fuse it had left.
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void ReceiveBananaCatch (float fuseSecondsLeft)
  {
    if (!IsMultiplayerAuthority() || !DrawingSlingshot || SlingshotAmmo != HeldWeapon.None) return;
    _grenadeFuseLeft = fuseSecondsLeft;
    LoadCosmeticAmmo (HeldWeapon.BananaGrenade);
    GD.Print ($"{DisplayName}: CAUGHT a live banana! {fuseSecondsLeft:0.0}s on the fuse...");
  }

  // Shooter side: the live projectile reported a catch.
  private void OnBananaCaught (Player catcher, float fuseSecondsLeft)
  {
    GD.Print ($"{DisplayName}: {catcher.DisplayName} caught my banana!");
    if (catcher.NetworkId == NetworkId) { ReceiveBananaCatch (fuseSecondsLeft); return; } // Our own, back in our own pouch.
    catcher.RpcId (catcher.NetworkId, MethodName.ReceiveBananaCatch, fuseSecondsLeft);
  }

  // Firing the caught grenade: a real banana leaves, fuse lit & carried over, ours to
  // blast & stick with. Called from FireSlingshotStone in place of a stone.
  private void FireGrenade (Vector3 origin, Vector3 direction)
  {
    var fuse = Mathf.Max (0.05f, _grenadeFuseLeft);
    SlingshotAmmo = HeldWeapon.None;
    SpawnGrenade (origin, direction, fuse, isLive: true);
    Rpc (MethodName.SpawnVisualGrenade, origin, direction, fuse);
  }

  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void SpawnVisualGrenade (Vector3 origin, Vector3 direction, float fuse) => SpawnGrenade (origin, direction, fuse, isLive: false);

  private void SpawnGrenade (Vector3 origin, Vector3 direction, float fuse, bool isLive)
  {
    var banana = _bananaProjectileScene.Instantiate <BananaProjectile>();
    GetParent().AddChild (banana);
    banana.LaunchLit (origin, direction, isLive, this, fuse);
    if (!isLive) return;
    banana.Exploded += OnBananaExploded;
    banana.StuckToPlayer += OnBananaStuck;
    banana.CaughtBySlingshot += OnBananaCaught; // It can be caught right back.
  }

  // Too slow: it detonates in the pouch - our own blast, reported like any blast of ours.
  private void GrenadeGoesOffInThePouch()
  {
    SlingshotAmmo = HeldWeapon.None;
    GD.Print ($"{DisplayName}: the banana went off in my slingshot!");
    Rpc (MethodName.ShowPouchBlast, GlobalPosition);
    ShowPouchBlast (GlobalPosition);
    OnBananaExploded (GlobalPosition);
  }

  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void ShowPouchBlast (Vector3 origin) => BananaProjectile.SpawnExplosionEffects (GetParent(), origin);
}
