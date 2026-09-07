// Only pre.9 shipping PNGs replaced by JPEGs belong in this allowlist.
// Called after successful payload installation; never remove a PNG whose
// replacement is missing. Transparent scenes and all flags/icons stay intact.
procedure RemoveReplacedLocationPng(const Stem: String);
var
  LegacyPath: String;
  ReplacementPath: String;
begin
  LegacyPath := ExpandConstant('{app}\assets\locations\') + Stem + '.png';
  ReplacementPath := ExpandConstant('{app}\assets\locations\') + Stem + '.jpg';
  if FileExists(ReplacementPath) and FileExists(LegacyPath) then
    if not DeleteFile(LegacyPath) then
      Log('Could not remove replaced location PNG: ' + Stem);
end;

procedure RemoveLegacyLocationPngs();
begin
  RemoveReplacedLocationPng('Bulgaria1');
  RemoveReplacedLocationPng('Bulgaria2');
  RemoveReplacedLocationPng('Bulgaria3');
  RemoveReplacedLocationPng('China');
  RemoveReplacedLocationPng('Discord1');
  RemoveReplacedLocationPng('Discord2');
  RemoveReplacedLocationPng('Discord3');
  RemoveReplacedLocationPng('Discord4');
  RemoveReplacedLocationPng('EUALL');
  RemoveReplacedLocationPng('Estonia');
  RemoveReplacedLocationPng('Finland1');
  RemoveReplacedLocationPng('Finland3');
  RemoveReplacedLocationPng('France1');
  RemoveReplacedLocationPng('France2');
  RemoveReplacedLocationPng('France3');
  RemoveReplacedLocationPng('Germany1');
  RemoveReplacedLocationPng('Germany2');
  RemoveReplacedLocationPng('Germany3');
  RemoveReplacedLocationPng('Germany4');
  RemoveReplacedLocationPng('Germany5');
  RemoveReplacedLocationPng('India1');
  RemoveReplacedLocationPng('India2');
  RemoveReplacedLocationPng('India3');
  RemoveReplacedLocationPng('Ireland');
  RemoveReplacedLocationPng('Italy');
  RemoveReplacedLocationPng('KZ1');
  RemoveReplacedLocationPng('KZ2');
  RemoveReplacedLocationPng('KZ3');
  RemoveReplacedLocationPng('Latvia1');
  RemoveReplacedLocationPng('Latvia2');
  RemoveReplacedLocationPng('Latvia3');
  RemoveReplacedLocationPng('Lithuania1');
  RemoveReplacedLocationPng('Lithuania2');
  RemoveReplacedLocationPng('Lithuania3');
  RemoveReplacedLocationPng('Netherlands2');
  RemoveReplacedLocationPng('Netherlands4');
  RemoveReplacedLocationPng('Netherlands5');
  RemoveReplacedLocationPng('Poland1');
  RemoveReplacedLocationPng('Poland2');
  RemoveReplacedLocationPng('Poland3');
  RemoveReplacedLocationPng('Portugal');
  RemoveReplacedLocationPng('RUALL');
  RemoveReplacedLocationPng('RUSSIA2');
  RemoveReplacedLocationPng('Russia1');
  RemoveReplacedLocationPng('Russia3');
  RemoveReplacedLocationPng('Spain');
  RemoveReplacedLocationPng('Sweden1');
  RemoveReplacedLocationPng('Sweden2');
  RemoveReplacedLocationPng('Sweden3');
  RemoveReplacedLocationPng('TR1');
  RemoveReplacedLocationPng('TR2');
  RemoveReplacedLocationPng('TR3');
  RemoveReplacedLocationPng('UK1');
  RemoveReplacedLocationPng('UK2');
  RemoveReplacedLocationPng('UK3');
  RemoveReplacedLocationPng('USA1');
  RemoveReplacedLocationPng('USA2');
  RemoveReplacedLocationPng('USA3');
  RemoveReplacedLocationPng('Ukraine1');
  RemoveReplacedLocationPng('Universal');
  RemoveReplacedLocationPng('finland2');
  RemoveReplacedLocationPng('ireland1');
  RemoveReplacedLocationPng('ireland3');
  RemoveReplacedLocationPng('netherlands1');
  RemoveReplacedLocationPng('netherlands3');
  RemoveReplacedLocationPng('universe');
  RemoveReplacedLocationPng('welocmescreengirl');
end;
