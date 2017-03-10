# TODO:
# * front-end for Tumblr session auth
# * callback page for Tumblr auth
# * page for archiving
# * page for deletions

from flask import Flask, render_template, request, session, redirect, url_for
import iso8601
import shutil

from appconfig import read_config
import tumblr_auth
from tumblr_utils import TumblrUtils


conf = read_config()
app = Flask('ninlil')
app.config.update(
    DEBUG = True,
    SECRET_KEY = conf['flask_secret_key'],
)


@app.route('/')
def index():
    return render_template('index.jinja.html')


@app.route('/tumblr')
def tumblr_start():
    return render_template('tumblr.jinja.html')


@app.route('/tumblr_save_auth', methods=['POST'])
def tumblr_save_auth():
    '''
    Given the user's blog URL, 
    start OAuth and redirect to Tumblr's auth URL.
    '''

    start_date_str = request.values.get('start_date')
    if start_date_str is None or start_date_str == '':
        start_date = None
    else: 
        start_date = iso8601.parse_date(start_date_str)
        # note: to go back from datetime to ISO 8601: dt.isoformat()

    end_date_str = request.values.get('end_date')
    if end_date_str is None or end_date_str == '':
        end_date = None
    else: 
        end_date = iso8601.parse_date(end_date_str)

    session['tumblr_save__blog_url'] = request.values.get('blog_url')
    session['tumblr_save__email'] = request.values.get('email')
    session['tumblr_save__start_date'] = start_date
    session['tumblr_save__end_date'] = end_date

    'Set up an auth URL which will redirect back to another of our routes.'
    next_route = url_for('tumblr_save_confirm', _external = True)
    auth_url, auth_token_secret = tumblr_auth.oauth_initial_auth(next_route)

    'Save the secret.'
    session['tumblr_save__oauth_token_secret'] = auth_token_secret

    'Proceed with OAuth.'
    return redirect(auth_url)


@app.route('/tumblr_save_confirm', methods=['GET'])
def tumblr_save_confirm():
    '''
    Given an OAuth token and verifier from Tumblr (and our secret from the session),
    derive the final OAuth token and verifier,
    then save photos to a file and provide a confirmation/download page.

    TODO:
    * Spin off something async which emails users.
    * Detect duplicates before re-downloading.
    * Perhaps use days instead of seconds, add a requesting session ID to the filename, and use filenames to detect duplicates?
    
    LATER TODO:
    * Garbage-collect archives for download after 24 or 36 hours.
    * Offload to S3.

    The session-munging to avoid duplicate downloads will cause trouble
    if someone tries concurrent archiving of different date ranges.
    '''

    'Check for duplicate sessions.'
    if session.get('tumblr_save__download_started', False):
        return render_template('tumblr_save_confirm.jinja.html', zipfile = None, status = 'A duplicate archive is in progress!')

    duplicate_zipfile = session.get('tumblr_save__zipfile')
    if not duplicate_zipfile is None:
        return render_template('tumblr_save_confirm.jinja.html', zipfile = duplicate_zipfile, status = 'A duplicate archive is ready!')

    'Update session for downloading.'
    session['tumblr_save__download_started'] = True

    'Finish OAuth.'
    final_oauth_token, final_oauth_token_secret = tumblr_auth.oauth_verify_user_token(
        session['tumblr_save__oauth_token_secret'],
        request.values.get('oauth_token'),
        request.values.get('oauth_verifier'),
    )
    session['tumblr_save__final_oauth_token'] = final_oauth_token
    session['tumblr_save__final_oauth_token_secret'] = final_oauth_token_secret

    'Download photos.'
    tu = TumblrUtils(
        final_oauth_token,
        final_oauth_token_secret,
        session['tumblr_save__blog_url'],
    )
    zipfile_tmp = tu.save_photos(
        session['tumblr_save__start_date'],
        session['tumblr_save__end_date']
    )

    'Move the ZIP for downloading.'
    # FIXME: delete or overwrite duplicates
    zipfile = shutil.move(zipfile_tmp, 'static/downloads/')

    'Update the session to reduce duplicate archiving.'
    session['tumblr_save__zipfile'] = zipfile

    return render_template('tumblr_save_confirm.jinja.html', zipfile = zipfile)


@app.route('/tumblr_delete_auth', methods=['POST'])
def tumblr_delete_auth():
    pass


@app.route('/tumblr_delete_confirm', methods=['POST'])
def tumblr_delete_confirm():
    pass


if __name__ == '__main__':
    app.run('localhost')
