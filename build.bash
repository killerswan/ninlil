#!/bin/bash

EXE=Ninlil.exe
if [ -f "$EXE" ]
then
   mv "$EXE" "$EXE.old"
fi

fsharpc --nologo --optimize --out:"$EXE" Tumblr.fs Ninlil.fs
