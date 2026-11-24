#!/usr/bin/env python3
"""Fake PLC: a small Modbus TCP server that plays the PLC for the FactorySim app.

It offers the same bit areas as the CODESYS "Modbus TCP Slave Device" in the
acceptance project:

    coils            written by the app  -> inputs of the PLC program  (FC01, FC05, FC15)
    discrete inputs  read by the app     <- outputs of the PLC program (FC02)
    holding registers written by the app -> INT inputs of the PLC       (FC03, FC06, FC16)
    input registers  read by the app     <- INT outputs of the PLC      (FC04)

Modes:
    echo    every coil is copied to the discrete input with the same offset, and
            every holding register to the input register with the same offset
    sorter  the start/stop + sort-on-height logic of the acceptance program

Standard library only, Python 3.9+. Binds to 127.0.0.1 and nothing else:
Modbus TCP has no authentication (see docs/SECURITY.md).

    py tools/fake_plc/fake_plc.py --mode sorter --port 5020
"""

import argparse
import socketserver
import struct
import sys
import threading
import time

HOST = "127.0.0.1"

# Coil offsets (app -> PLC), same as tagmap.json.
I_START, I_STOP, I_AT_PUSHER, I_HIGH_BOX, I_PUSHER_BACK, I_PUSHER_FRONT, I_SIM_RUNNING = range(7)
# Discrete input offsets (PLC -> app).
O_CONVEYOR, O_PUSHER, O_EMITTER, O_RUN_LAMP = range(4)

ILLEGAL_FUNCTION = 1
ILLEGAL_DATA_ADDRESS = 2
ILLEGAL_DATA_VALUE = 3


class Memory:
    """The PLC's process image, shared between the server threads and the scan loop."""

    def __init__(self, size):
        self.lock = threading.Lock()
        self.coils = [False] * size
        self.inputs = [False] * size
        self.holding = [0] * size        # 16-bit words, 0..65535
        self.input_registers = [0] * size

    def read(self, area, address, count):
        with self.lock:
            return list(area[address:address + count])

    def write_coils(self, address, values):
        with self.lock:
            self.coils[address:address + len(values)] = values

    def write_holding(self, address, values):
        with self.lock:
            self.holding[address:address + len(values)] = values


class Sorter:
    """Scan-cycle logic: start/stop latch, push high boxes aside, 3 s watchdog on the pusher."""

    PUSH_TIMEOUT_S = 3.0

    def __init__(self):
        self.run = False
        self.step = 0          # 0 = transport, 10 = push, 20 = return
        self.fault = False
        self.push_started = None
        self.sorted_count = 0

    def scan(self, i, now):
        if i[I_STOP] or not i[I_SIM_RUNNING] or self.fault:
            self.run = False
        elif i[I_START]:
            self.run = True

        if self.fault and i[I_START] and not i[I_STOP]:
            self.fault = False
            self.step = 0

        if self.step == 0:
            if self.run and i[I_AT_PUSHER] and i[I_HIGH_BOX] and i[I_PUSHER_BACK]:
                self.step = 10
        elif self.step == 10:
            if i[I_PUSHER_FRONT]:
                self.sorted_count += 1
                self.step = 20
        elif self.step == 20:
            if i[I_PUSHER_BACK]:
                self.step = 0

        # Watchdog, like a TON over steps 10 and 20. It only runs while the simulation runs,
        # so a paused scene does not trip it.
        if self.step in (10, 20) and i[I_SIM_RUNNING]:
            if self.push_started is None:
                self.push_started = now
            elif now - self.push_started > self.PUSH_TIMEOUT_S:
                self.fault = True
                self.step = 0
                self.push_started = None
        else:
            self.push_started = None

        outputs = [False] * 4
        outputs[O_CONVEYOR] = self.run and self.step == 0
        outputs[O_PUSHER] = self.step == 10
        outputs[O_EMITTER] = self.run
        outputs[O_RUN_LAMP] = self.run
        return outputs


def use_fine_timer():
    """Windows rounds sleeps up to 15.6 ms by default; ask for 1 ms so a 10 ms scan is 10 ms."""
    if sys.platform == "win32":
        try:
            import ctypes
            ctypes.windll.winmm.timeBeginPeriod(1)
        except (OSError, AttributeError):
            pass


def scan_loop(memory, mode, scan_s, stop):
    use_fine_timer()
    sorter = Sorter()
    next_scan = time.monotonic()
    while not stop.is_set():
        with memory.lock:
            if mode == "echo":
                memory.inputs[:] = memory.coils
                memory.input_registers[:] = memory.holding
            else:
                outputs = sorter.scan(memory.coils, time.monotonic())
                memory.inputs[:len(outputs)] = outputs
        next_scan += scan_s
        delay = next_scan - time.monotonic()
        if delay > 0:
            stop.wait(delay)
        else:
            next_scan = time.monotonic()


def pack_bits(bits):
    data = bytearray((len(bits) + 7) // 8)
    for index, bit in enumerate(bits):
        if bit:
            data[index // 8] |= 1 << (index % 8)
    return bytes(data)


def unpack_bits(data, count):
    return [bool(data[index // 8] >> (index % 8) & 1) for index in range(count)]


class Handler(socketserver.BaseRequestHandler):
    def handle(self):
        self.request.setsockopt(6, 1, 1)  # IPPROTO_TCP, TCP_NODELAY
        memory = self.server.memory
        while True:
            header = self._receive(7)
            if header is None:
                return
            transaction, protocol, length, unit = struct.unpack(">HHHB", header)
            if protocol != 0 or length < 2 or length > 254:
                return
            pdu = self._receive(length - 1)
            if pdu is None:
                return
            response = self._execute(memory, pdu)
            self.request.sendall(struct.pack(">HHHB", transaction, 0, len(response) + 1, unit) + response)

    def _receive(self, count):
        data = b""
        while len(data) < count:
            try:
                chunk = self.request.recv(count - len(data))
            except OSError:
                return None
            if not chunk:
                return None
            data += chunk
        return data

    @staticmethod
    def _execute(memory, pdu):
        function = pdu[0]
        size = len(memory.coils)

        def error(code):
            return bytes([function | 0x80, code])

        try:
            if function in (1, 2):  # read coils / read discrete inputs
                address, count = struct.unpack(">HH", pdu[1:5])
                if not 1 <= count <= 2000:
                    return error(ILLEGAL_DATA_VALUE)
                if address + count > size:
                    return error(ILLEGAL_DATA_ADDRESS)
                area = memory.coils if function == 1 else memory.inputs
                data = pack_bits(memory.read(area, address, count))
                return bytes([function, len(data)]) + data

            if function == 5:  # write single coil
                address, value = struct.unpack(">HH", pdu[1:5])
                if value not in (0x0000, 0xFF00):
                    return error(ILLEGAL_DATA_VALUE)
                if address >= size:
                    return error(ILLEGAL_DATA_ADDRESS)
                memory.write_coils(address, [value == 0xFF00])
                return pdu[:5]

            if function == 15:  # write multiple coils
                address, count, byte_count = struct.unpack(">HHB", pdu[1:6])
                if not 1 <= count <= 1968 or byte_count != (count + 7) // 8 or len(pdu) < 6 + byte_count:
                    return error(ILLEGAL_DATA_VALUE)
                if address + count > size:
                    return error(ILLEGAL_DATA_ADDRESS)
                memory.write_coils(address, unpack_bits(pdu[6:6 + byte_count], count))
                return pdu[:5]

            if function in (3, 4):  # read holding registers / read input registers
                address, count = struct.unpack(">HH", pdu[1:5])
                if not 1 <= count <= 125:
                    return error(ILLEGAL_DATA_VALUE)
                if address + count > size:
                    return error(ILLEGAL_DATA_ADDRESS)
                area = memory.holding if function == 3 else memory.input_registers
                words = memory.read(area, address, count)
                return bytes([function, 2 * count]) + struct.pack(">" + "H" * count, *words)

            if function == 6:  # write single register
                address, value = struct.unpack(">HH", pdu[1:5])
                if address >= size:
                    return error(ILLEGAL_DATA_ADDRESS)
                memory.write_holding(address, [value])
                return pdu[:5]

            if function == 16:  # write multiple registers
                address, count, byte_count = struct.unpack(">HHB", pdu[1:6])
                if not 1 <= count <= 123 or byte_count != 2 * count or len(pdu) < 6 + byte_count:
                    return error(ILLEGAL_DATA_VALUE)
                if address + count > size:
                    return error(ILLEGAL_DATA_ADDRESS)
                memory.write_holding(address, list(struct.unpack(">" + "H" * count, pdu[6:6 + byte_count])))
                return pdu[:5]
        except struct.error:
            return error(ILLEGAL_DATA_VALUE)

        return error(ILLEGAL_FUNCTION)


class Server(socketserver.ThreadingTCPServer):
    allow_reuse_address = True
    daemon_threads = True


def main():
    parser = argparse.ArgumentParser(description="Fake PLC for FactorySim (Modbus TCP server on 127.0.0.1).")
    parser.add_argument("--mode", choices=["echo", "sorter"], default="sorter")
    parser.add_argument("--port", type=int, default=5020, help="TCP port; 0 picks a free one")
    parser.add_argument("--scan-ms", type=float, default=10.0, help="scan cycle in milliseconds")
    parser.add_argument("--size", type=int, default=64, help="number of coils, discrete inputs and of each kind of register")
    args = parser.parse_args()

    memory = Memory(args.size)
    stop = threading.Event()
    with Server((HOST, args.port), Handler) as server:
        server.memory = memory
        threading.Thread(target=scan_loop, args=(memory, args.mode, args.scan_ms / 1000.0, stop), daemon=True).start()
        # The integration tests wait for this line to learn the port.
        print("READY port={} mode={} scan={}ms".format(server.server_address[1], args.mode, args.scan_ms), flush=True)
        try:
            server.serve_forever(poll_interval=0.1)
        except KeyboardInterrupt:
            pass
        finally:
            stop.set()
    return 0


if __name__ == "__main__":
    sys.exit(main())
