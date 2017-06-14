#!flask/bin/python
from flask import Flask, jsonify
from os import listdir
from os.path import isfile, join

app = Flask(__name__)

@app.route('/mydrive/list', methods=['GET'])
def get_filelist():
    mypath = "data/"
    onlyfiles = [f for f in listdir(mypath) if isfile(join(mypath, f))]
    for fname in onlyfiles:
        print fname
    return jsonify({'tasks': tasks})


if __name__ == '__main__':
    app.run(debug=True)

