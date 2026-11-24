(* Global Variable List "IO" — eigen werk, hoort bij de MVP-scène van FactorySim.
   Plak dit in een GVL met de naam IO. De volgorde en namen komen overeen met
   src/FactorySim.App/tagmap.json. *)
VAR_GLOBAL
    (* App -> PLC: Coils 0..7 van het Modbus TCP Slave Device *)
    iStart       : BOOL;    (* Coil 0: startknop (puls) *)
    iStop        : BOOL;    (* Coil 1: stopknop, TRUE = ingedrukt *)
    iAtPusher    : BOOL;    (* Coil 2: photo-eye, doos voor de pusher *)
    iHighBox     : BOOL;    (* Coil 3: hoge photo-eye, doos is hoog *)
    iPusherBack  : BOOL;    (* Coil 4: pusher ingetrokken *)
    iPusherFront : BOOL;    (* Coil 5: pusher uitgeschoven *)
    iSimRunning  : BOOL;    (* Coil 6: simulatie draait *)
    iSpare       : BOOL;    (* Coil 7: reserve *)

    (* PLC -> App: Discrete Inputs 0..7 van het Modbus TCP Slave Device *)
    oConveyor    : BOOL;    (* Discrete Input 0: band aan *)
    oPusher      : BOOL;    (* Discrete Input 1: pusher uitschuiven *)
    oEmitter     : BOOL;    (* Discrete Input 2: dozen spawnen *)
    oRunLamp     : BOOL;    (* Discrete Input 3: lamp in bedrijf *)
    oSpare1      : BOOL;
    oSpare2      : BOOL;
    oSpare3      : BOOL;
    oSpare4      : BOOL;
END_VAR
