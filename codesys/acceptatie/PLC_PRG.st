(* Acceptatieprogramma voor de MVP-scène van FactorySim — eigen werk.
   Start/stop en sorteren op hoogte: hoge dozen gaan via de pusher naar de zijbaan,
   lage dozen rijden door. Dezelfde logica zit in tools/fake_plc/fake_plc.py (modus sorter).

   In CODESYS: het declaratiedeel hieronder in het bovenste venster van PLC_PRG plakken,
   het programmadeel in het onderste. *)

(* ---------- Declaratie ---------- *)
PROGRAM PLC_PRG
VAR
    bRun    : BOOL;         (* installatie in bedrijf *)
    nStep   : INT := 0;     (* 0 = transport, 10 = duwen, 20 = terug *)
    tonPush : TON;          (* bewaking: pusher haalt zijn eindstand niet *)
    bFault  : BOOL;
    nSorted : UDINT;        (* aantal uitgesorteerde dozen *)
END_VAR

(* ---------- Programma ---------- *)

(* Start/stop: stop dominant, alleen als de simulatie draait *)
IF IO.iStop OR NOT IO.iSimRunning OR bFault THEN
    bRun := FALSE;
ELSIF IO.iStart THEN
    bRun := TRUE;
END_IF

(* Fout resetten met Start terwijl Stop is losgelaten *)
IF bFault AND IO.iStart AND NOT IO.iStop THEN
    bFault := FALSE;
    nStep  := 0;
END_IF

(* Sorteerstap *)
CASE nStep OF
    0:  (* transport *)
        IF bRun AND IO.iAtPusher AND IO.iHighBox AND IO.iPusherBack THEN
            nStep := 10;
        END_IF

    10: (* band stil, pusher uit *)
        IF IO.iPusherFront THEN
            nSorted := nSorted + 1;
            nStep := 20;
        END_IF

    20: (* pusher terug *)
        IF IO.iPusherBack THEN
            nStep := 0;
        END_IF
END_CASE

(* Bewaking: de pusher moet binnen 3 s zijn eindstand halen.
   Alleen terwijl de simulatie draait, zodat pauzeren geen fout geeft. *)
tonPush(IN := ((nStep = 10) OR (nStep = 20)) AND IO.iSimRunning, PT := T#3S);
IF tonPush.Q THEN
    bFault := TRUE;
    nStep  := 0;
END_IF

(* Uitgangen *)
IO.oConveyor := bRun AND (nStep = 0);
IO.oPusher   := (nStep = 10);
IO.oEmitter  := bRun;
IO.oRunLamp  := bRun;
